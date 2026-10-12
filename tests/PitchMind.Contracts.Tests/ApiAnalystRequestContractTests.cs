using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class ApiAnalystRequestContractTests : IAsyncLifetime
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
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["Runtime__Profile"] = "local";
        foreach (var argument in new[]
                 {
                     "run", "--no-build", "--configuration", configuration, "--project", apiProject,
                     "--", "--urls", address
                 })
            start.ArgumentList.Add(argument);

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

    [Theory]
    [InlineData("{\"insightId\":", "INVALID_REQUEST")]
    [InlineData("[]", "INVALID_REQUEST")]
    [InlineData("{\"insightId\":\"\",\"atSeconds\":10}", "INVALID_REQUEST")]
    [InlineData("{\"insightId\":7,\"atSeconds\":10}", "INVALID_REQUEST")]
    [InlineData("{\"insightId\":\"missing\"}", "INVALID_REQUEST")]
    [InlineData("{\"insightId\":\"missing\",\"atSeconds\":\"10\"}", "INVALID_REQUEST")]
    [InlineData("{\"insightId\":\"missing\",\"atSeconds\":10.5}", "INVALID_REQUEST")]
    [InlineData("{\"insightId\":\"missing\",\"atSeconds\":-1}", "INVALID_TIME")]
    [InlineData("{\"insightId\":\"missing\",\"atSeconds\":5401}", "INVALID_TIME")]
    [InlineData("{\"insightId\":\"missing\",\"atSeconds\":2147483648}", "INVALID_TIME")]
    [InlineData("{\"insightId\":\"missing\",\"atSeconds\":9999999999999999999999999999999999999999}", "INVALID_TIME")]
    [InlineData("{\"insightId\":\"missing\",\"atSeconds\":10,\"audience\":\"coach\"}", "INVALID_REQUEST")]
    [InlineData("{\"insightId\":\"missing\",\"atSeconds\":10,\"audience\":3}", "INVALID_REQUEST")]
    public async Task Invalid_analyst_request_fields_return_a_typed_problem(string body, string code)
    {
        using var response = await Client.PostAsync(AnalystPath, new StringContent(body, Encoding.UTF8, "application/json"));

        await AssertProblem(response, HttpStatusCode.BadRequest, code);
    }

    [Theory]
    [InlineData("{\"insightId\":\"missing\",\"atSeconds\":0}")]
    [InlineData("{\"insightId\":\"missing\",\"atSeconds\":5400,\"audience\":\"analyst\"}")]
    public async Task Valid_analyst_request_shape_still_fails_closed_without_canonical_data(string body)
    {
        using var response = await Client.PostAsync(AnalystPath, new StringContent(body, Encoding.UTF8, "application/json"));

        await AssertProblem(response, HttpStatusCode.ServiceUnavailable, "MATCH_DATA_UNAVAILABLE");
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
}
