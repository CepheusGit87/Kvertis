namespace Kvertis.App.Scenes;

/// <summary>
/// Limits of the swirl surface (worksheet "Übergänge", section "Leistung"). <see cref="MaxParticles"/> is hard:
/// pixels, ring grains, dust and sparks together never exceed it. <see cref="RingGrains"/> is the pool the dust
/// rings of the white hole (from 150 files on) share; with two pixel streams of 744 it stays under the cap.
/// </summary>
public sealed record SwirlBudget(
    int MaxSwirlFiles = 2,
    int MaxStackSheets = 24,
    int MaxEmptyPlaces = 150,
    int Dust = 120,
    int Sparks = 320,
    int StarsNear = 70,
    int StarsFull = 110,
    int MaxParticles = 4000,
    int RingGrains = 2400);
