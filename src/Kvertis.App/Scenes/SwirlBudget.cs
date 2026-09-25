namespace Kvertis.App.Scenes;

/// <summary>
/// Limits of the swirl surface (worksheet "Übergänge", section "Leistung"). <see cref="MaxParticles"/> is hard:
/// pixels, dust and sparks together never exceed it.
/// </summary>
public sealed record SwirlBudget(
    int MaxSwirlFiles = 2,
    int MaxStackSheets = 24,
    int MaxEmptyPlaces = 150,
    int Dust = 120,
    int Sparks = 320,
    int StarsNear = 70,
    int StarsFull = 110,
    int MaxParticles = 4000);
