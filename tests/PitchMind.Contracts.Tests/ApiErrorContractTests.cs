using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PitchMind.Api;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class ApiErrorContractTests : IAsyncLifetime
{
    private Process? _api;
    private HttpClient? _client;
    private string? _analystPath;
    private readonly StringBuilder _output = new();

    public async Task InitializeAsync()
    {
        var repository = FindRepositoryRoot();
        var apiProject = Path.Combine(repository, "src", "PitchMind.Api", "PitchMind.Api.csproj");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent?.Name ?? "Release";
        var port = ReserveLoopbackPort();
        var address = $"http://127.0.0.1:{port}";
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repository,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
                 {
                     "run", "--no-build", "--configuration", configuration, "--project", apiProject,
                     "--", "--urls", address
                 })
            start.ArgumentList.Add(argument);

        start.Environment["DOTNET_ENVIRONMENT"] = "Development";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["Runtime__Profile"] = "local";

        _api = Process.Start(start) ?? throw new InvalidOperationException("Could not start the local API process.");
        _api.OutputDataReceived += (_, eventArgs) => AppendOutput(eventArgs.Data);
        _api.ErrorDataReceived += (_, eventArgs) => AppendOutput(eventArgs.Data);
        _api.BeginOutputReadLine();
        _api.BeginErrorReadLine();
        _client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(3) };

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_api.HasExited)
                throw new InvalidOperationException($"Local API exited before becoming healthy. Output: {Output}");
            try
            {
                using var response = await _client.GetAsync("/health");
                if (response.IsSuccessStatusCode)
                {
                    _analystPath = $"/api/v1/matches/{Guid.NewGuid()}/analyst";
                    return;
                }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(200);
        }

        throw new TimeoutException($"Local API did not become healthy. Output: {Output}");
    }

    [Fact]
    public void Argument_exception_details_are_not_exposed_in_the_problem_response()
    {
        const string sensitiveMessage = "provider payload with private input";
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "correlation-id";
        context.Request.Path = "/api/v1/matches";

        var problem = ApiArgumentErrorResponse.Create(context, new ArgumentException(sensitiveMessage));
        var json = JsonSerializer.Serialize(problem, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("INVALID_REQUEST", problem.Code);
        Assert.Null(problem.Detail);
        Assert.DoesNotContain(sensitiveMessage, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Oversized_analyst_request_returns_the_versioned_problem_contract()
    {
        var padding = new string('x', 5000);
        var body = $"{{\"insightId\":\"missing\",\"atSeconds\":0,\"audience\":\"fan\",\"padding\":\"{padding}\"}}";
        using var response = await Client.PostAsync(AnalystPath, new StringContent(body, Encoding.UTF8, "application/json"));

        await AssertProblem(response, HttpStatusCode.RequestEntityTooLarge, "REQUEST_TOO_LARGE");
    }

    [Fact]
    public async Task Oversized_chunked_analyst_request_returns_the_versioned_problem_contract()
    {
        var padding = new string('x', 5000);
        var body = Encoding.UTF8.GetBytes($"{{\"insightId\":\"missing\",\"atSeconds\":0,\"audience\":\"fan\",\"padding\":\"{padding}\"}}");
        using var response = await Client.PostAsync(AnalystPath, new ChunkedContent(body));

        await AssertProblem(response, HttpStatusCode.RequestEntityTooLarge, "REQUEST_TOO_LARGE");
    }

    [Fact]
    public async Task Analyst_rate_limit_returns_the_versioned_problem_contract()
    {
        HttpResponseMessage? last = null;
        try
        {
            for (var request = 0; request < 31; request++)
            {
                last?.Dispose();
                last = await Client.PostAsync(
                    AnalystPath,
                    new StringContent("{\"insightId\":\"missing\",\"atSeconds\":0,\"audience\":\"fan\"}", Encoding.UTF8, "application/json"));
            }

            Assert.NotNull(last);
            await AssertProblem(last, HttpStatusCode.TooManyRequests, "RATE_LIMITED");
        }
        finally
        {
            last?.Dispose();
        }
    }

    [Fact]
    public async Task Malformed_analyst_request_returns_the_versioned_problem_contract()
    {
        using var response = await Client.PostAsync(AnalystPath, new StringContent("{", Encoding.UTF8, "application/json"));

        await AssertProblem(response, HttpStatusCode.BadRequest, "INVALID_REQUEST");
    }

    [Fact]
    public async Task Unsupported_analyst_content_type_returns_the_versioned_problem_contract()
    {
        using var response = await Client.PostAsync(AnalystPath, new StringContent("{}", Encoding.UTF8, "text/plain"));

        await AssertProblem(response, HttpStatusCode.UnsupportedMediaType, "UNSUPPORTED_MEDIA_TYPE");
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_api is not null)
        {
            if (!_api.HasExited)
                _api.Kill(entireProcessTree: true);
            await _api.WaitForExitAsync();
            _api.Dispose();
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "PitchMind.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate PitchMind.slnx from the contract test output directory.");
    }

    private static int ReserveLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private void AppendOutput(string? line)
    {
        if (line is not null)
            lock (_output) _output.AppendLine(line);
    }

    private string Output
    {
        get { lock (_output) return _output.ToString(); }
    }

    private HttpClient Client => _client ?? throw new InvalidOperationException("The API client was not initialized.");
    private string AnalystPath => _analystPath ?? throw new InvalidOperationException("The analyst route was not initialized.");

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = document.RootElement;
        Assert.Equal((int)expectedStatus, body.GetProperty("status").GetInt32());
        Assert.Equal(expectedCode, body.GetProperty("code").GetString());
        Assert.Equal(response.Headers.GetValues("X-Correlation-ID").Single(), body.GetProperty("correlationId").GetString());
    }

    private sealed class ChunkedContent(byte[] body) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(body).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
