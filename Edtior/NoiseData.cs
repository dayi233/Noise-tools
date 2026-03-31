using UnityEngine;

public static class NoiseData
{
    public static float Scale { get; set; } = 1.0f;
    public static int Seed { get; set; } = 0;
    public static int Width { get; set; } = 256;
    public static int Height { get; set; } = 256;
    public static int Depth { get; set; } = 256;
    public static NoiseCreate.NoiseDimension SelectedDimension { get; set; } = NoiseCreate.NoiseDimension.TwoD;
    public static NoiseCreate.NoiseType SelectedNoiseType { get; set; } = NoiseCreate.NoiseType.Perlin;
    public static int Octaves { get; set; } = 6;
    public static float Lacunarity { get; set; } = 2.0f;
    public static float Persistence { get; set; } = 0.5f;
}


