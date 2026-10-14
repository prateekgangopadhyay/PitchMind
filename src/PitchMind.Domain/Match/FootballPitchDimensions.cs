namespace PitchMind.Domain.Match;

/// <summary>Bounds supported by PitchMind's metre-based football pitch contracts.</summary>
public static class FootballPitchDimensions
{
    public static bool AreValid(double lengthMetres, double widthMetres) =>
        double.IsFinite(lengthMetres) && double.IsFinite(widthMetres) &&
        lengthMetres is >= 90 and <= 120 && widthMetres is >= 45 and <= 90 &&
        lengthMetres > widthMetres;
}
