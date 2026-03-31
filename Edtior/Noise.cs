using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine;
using UnityEngine.Rendering;  // 着色器相关数学
using UnityEngine.UI;  // 如果是UI相关的插值
using static NoiseCreate;
public delegate float NoiseFunction(int seed, float x, float y, float z);


public class NoiseGenerator : MonoBehaviour
{
    private static Dictionary<(NoiseDimension, NoiseType), NoiseFunction> _noiseLookup;
    [Header("通用参数")]
    public float scale = NoiseData.Scale;
    public int seed = NoiseData.Seed;

    [Header("2D噪声参数")]
    public int width = NoiseData.Width;
    public int height = NoiseData.Height;

    [Header("3D噪声参数")]
    public int depth = NoiseData.Depth;

    [Header("噪声类型")]
    NoiseDimension dim = NoiseData.SelectedDimension;
    NoiseType type = NoiseData.SelectedNoiseType;

    [Header("FBM噪声")]
    public int octaves = NoiseData.Octaves;
    public float lacunarity = NoiseData.Lacunarity;
    public float persistence = NoiseData.Persistence;

    //初始化查找表
    private static void InitializeNoiseLookup()
    {
        if (_noiseLookup != null) return; // 避免重复初始化

        _noiseLookup = new Dictionary<(NoiseDimension, NoiseType), NoiseFunction>
        {
            // 2D 噪声映射
            { (NoiseDimension.TwoD, NoiseType.Perlin),   GetPerlinNoise2D },
            { (NoiseDimension.TwoD, NoiseType.Simplex),  GetSimplexNoise2D },
            { (NoiseDimension.TwoD, NoiseType.Value),    GetValueNoise2D },
            { (NoiseDimension.TwoD, NoiseType.Voronoi),  GetVoronoiNoise2D },
            { (NoiseDimension.TwoD, NoiseType.FBM_Perlin),  GetFBMNoise2D },
            
            // 3D 噪声映射
            { (NoiseDimension.ThreeD, NoiseType.Perlin),   GetPerlinNoise3D },
            { (NoiseDimension.ThreeD, NoiseType.Simplex),  GetSimplexNoise3D },
            { (NoiseDimension.ThreeD, NoiseType.Value),    GetValueNoise3D },
            { (NoiseDimension.ThreeD, NoiseType.Voronoi),  GetVoronoiNoise3D },
            { (NoiseDimension.ThreeD, NoiseType.FBM_Perlin),  GetFBMNoise3D }
        };
    }

    // 使用种子生成梯度向量
    private static Vector2 GetNoiseGradient2D(int x, int y, int seed)
    {
        // 使用经典的“噪声哈希”算法，确保结果在 0-1 之间
        int n = seed + x * 1619 + y * 31337;
        n = (n << 13) ^ n;
        float angle = (n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff;
        angle /= (float)0x80000000;

        // 将角度转换为单位向量
        float rad = angle * Mathf.PI * 2f;
        return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
    }
    //3D
    private static Vector3 GetNoiseGradient3D(int x, int y, int z, int seed)
    {
        // 1. 混合坐标生成唯一哈希值
        // 这里使用了三个不同的大质数来避免网格化
        int n = seed + x * 1619 + y * 31337 + z * 6971;
        n = (n << 13) ^ n; // 简单的位运算扰动

        // 2. 生成两个伪随机数
        // 我们需要两个独立的随机值来计算 3D 方向
        // 使用不同的多项式来从同一个 n 生成不同的序列
        int k1 = n * (n * n * 15731 + 789221) + 1376312589;
        int k2 = n * (n * n * 19263 + 840129) + 1024124197; // 不同的系数

        // 转换为 0-1 之间的浮点数
        // 使用位操作取绝对值并归一化 (0x7fffffff 是 int 最大正值)
        float u = (k1 & 0x7fffffff) / (float)0x80000000; // 0 ~ 1
        float v = (k2 & 0x7fffffff) / (float)0x80000000; // 0 ~ 1

        // 3. 球面坐标转换为笛卡尔坐标
        // u 对应方位角 theta (0 ~ 2PI)
        // v 对应余纬度 phi (0 ~ PI)
        float theta = u * Mathf.PI * 2f; // 0 ~ 2PI
        float phi = v * Mathf.PI;      // 0 ~ PI

        // 球面坐标公式:
        float sinPhi = Mathf.Sin(phi);
        return new Vector3(
            sinPhi * Mathf.Cos(theta),
            sinPhi * Mathf.Sin(theta),
            Mathf.Cos(phi)
        );
    }

    // 缓动函数 (Unity没有内置的Perlin缓动函数，需要保留)
    public static float Fade(float t)
    {
        return t * t * t * (t * (t * 6 - 15) + 10); // 6t^5 - 15t^4 + 10t^3
    }

    //生成图像
    public static Color GetPixelColor(int i, int j, int k)
    {
        // 初始化查找表 (第一次调用时)
        InitializeNoiseLookup();

        // 1. 坐标转换
        float noiseX = i * NoiseData.Scale + 0.001f;
        float noiseY = j * NoiseData.Scale + 0.001f;
        float noiseZ = (NoiseData.SelectedDimension == NoiseDimension.ThreeD)
            ? k * NoiseData.Scale + 0.001f
            : 0f;

        // 2. 核心逻辑：查表并调用函数
        // 构建 Key
        var key = (NoiseData.SelectedDimension, NoiseData.SelectedNoiseType);

        float noiseValue = 0f;

        // 查找并执行对应的噪声函数
        if (_noiseLookup.TryGetValue(key, out NoiseFunction noiseFunc))
        {
            noiseValue = noiseFunc(NoiseData.Seed, noiseX, noiseY, noiseZ);
        }
        else
        {
            // 安全回退 (例如配置缺失时)
            Debug.LogWarning($"未配置噪声类型: {NoiseData.SelectedDimension} - {NoiseData.SelectedNoiseType}");
            noiseValue = GetPerlinNoise2D(NoiseData.Seed, noiseX, noiseY, 0f);
        }

        // 3. 颜色映射
        float t = Mathf.Clamp01(noiseValue);
        return new Color(t, t, t);
    }

    // 1. 专门用于字典调用的 2D 生成函数
    // 符合 Func<Texture2D> 委托
    public static Texture2D GenerateTexture2D()
    {
        Texture2D texture = new Texture2D(NoiseData.Width, NoiseData.Height, TextureFormat.RGB24, false);
        texture.filterMode = FilterMode.Bilinear;
        Color[] pixels = new Color[NoiseData.Width * NoiseData.Height];

        for (int y = 0; y < NoiseData.Height; y++)
        {
            for (int x = 0; x < NoiseData.Width; x++)
            {
                int index = y * NoiseData.Width + x;
                // 强制为 2D 模式采样
                NoiseData.SelectedDimension = NoiseDimension.TwoD;
                pixels[index] = GetPixelColor(x, y, 0);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }
    // 2. 专门用于字典调用的 3D 生成函数
    // 符合 Func<Texture3D> 委托
    public static Texture3D GenerateTexture3D()
    {
        Texture3D texture = new Texture3D(NoiseData.Width, NoiseData.Height, NoiseData.Depth, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;

        Color[] colors = new Color[NoiseData.Width * NoiseData.Height * NoiseData.Depth];

        for (int z = 0; z < NoiseData.Depth; z++)
        {
            for (int y = 0; y < NoiseData.Height; y++)
            {
                for (int x = 0; x < NoiseData.Width; x++)
                {
                    int index = z * (NoiseData.Width * NoiseData.Height) + y * NoiseData.Width + x;
                    NoiseData.SelectedDimension = NoiseDimension.ThreeD;
                    colors[index] = GetPixelColor(x, y, z);
                }
            }
        }

        texture.SetPixels(colors);
        texture.Apply();
        return texture;
    }
    //2DHash函数
    public static float Hash2D(int x, int y, int seed)
    {
        int n = seed + x * 1619 + y * 31337;
        n = (n << 13) ^ n;
        uint result = (uint)(n + (n * n * 15731U) + (n * 789221U) + 1376312589U);
        return (result & 0x7fffffff) / (float)0x80000000;
    }
    //3DHash函数
    public static float Hash3D(int x, int y, int z, int seed)
    {
        int n = seed + x * 1619 + y * 31337 + z * 6971;
        n = (n << 13) ^ n;
        uint result = (uint)(n + (n * n * 15731U) + (n * 789221U) + 1376312589U);
        return (result & 0x7fffffff) / (float)0x80000000;
    }

    // 2D Perlin噪声
    public static float GetPerlinNoise2D(int seed, float x, float y, float z)
    {

        // 确定单位正方形的四个顶点坐标
        int x0 = Mathf.FloorToInt(x);
        int x1 = x0 + 1;
        int y0 = Mathf.FloorToInt(y);
        int y1 = y0 + 1;

        // 计算点在单位正方形内的位置
        float xf = x - x0;
        float yf = y - y0;

        // 应用缓动函数
        float u = Fade(xf);
        float v = Fade(yf);

        // 为四个顶点生成梯度向量
        // 使用不同的种子偏移确保每个顶点有唯一的梯度
        Vector2 gradient00 = GetNoiseGradient2D(x0, y0, seed);
        Vector2 gradient10 = GetNoiseGradient2D(x1, y0, seed);
        Vector2 gradient01 = GetNoiseGradient2D(x0, y1, seed);
        Vector2 gradient11 = GetNoiseGradient2D(x1, y1, seed);

        // 计算四个顶点到输入点的向量
        Vector2 distance00 = new Vector2(xf, yf);
        Vector2 distance10 = new Vector2(xf - 1.0f, yf);
        Vector2 distance01 = new Vector2(xf, yf - 1.0f);
        Vector2 distance11 = new Vector2(xf - 1.0f, yf - 1.0f);

        // 计算每个顶点的点积（梯度向量与距离向量的点积）
        float dot00 = Vector2.Dot(gradient00, distance00);
        float dot10 = Vector2.Dot(gradient10, distance10);
        float dot01 = Vector2.Dot(gradient01, distance01);
        float dot11 = Vector2.Dot(gradient11, distance11);

        // 双线性插值
        float xInterp0 = Mathf.Lerp(dot00, dot10, u);
        float xInterp1 = Mathf.Lerp(dot01, dot11, u);
        float result = Mathf.Lerp(xInterp0, xInterp1, v);

        // 将结果从[-1,1]映射到[0,1]
        return (result + 1f) / 2f;
    }

    // 3D Perlin噪声
    public static float GetPerlinNoise3D(int seed, float x, float y, float z)
    {
        // 1. 确定单位立方体的八个顶点坐标
        // 分别计算 x, y, z 轴的整数部分（最小顶点）
        int x0 = Mathf.FloorToInt(x);
        int x1 = x0 + 1; // x 轴最大顶点
        int y0 = Mathf.FloorToInt(y);
        int y1 = y0 + 1; // y 轴最大顶点
        int z0 = Mathf.FloorToInt(z);
        int z1 = z0 + 1; // z 轴最大顶点

        // 2. 计算点在单位立方体内的局部位置 (0~1)
        float xf = x - x0;
        float yf = y - y0;
        float zf = z - z0;

        // 3. 应用缓动函数 (消除线性插值的棱角)
        float u = Fade(xf); // X轴权重
        float v = Fade(yf); // Y轴权重
        float w = Fade(zf); // Z轴权重

        // 4. 为八个顶点生成梯度向量 (3D梯度通常是单位球面上的随机向量)
        // 格式: GetNoiseGradient(x, y, z, seed)
        Vector3 g000 = GetNoiseGradient3D(x0, y0, z0, seed);
        Vector3 g100 = GetNoiseGradient3D(x1, y0, z0, seed);
        Vector3 g010 = GetNoiseGradient3D(x0, y1, z0, seed);
        Vector3 g110 = GetNoiseGradient3D(x1, y1, z0, seed);

        Vector3 g001 = GetNoiseGradient3D(x0, y0, z1, seed);
        Vector3 g101 = GetNoiseGradient3D(x1, y0, z1, seed);
        Vector3 g011 = GetNoiseGradient3D(x0, y1, z1, seed);
        Vector3 g111 = GetNoiseGradient3D(x1, y1, z1, seed);

        // 5. 计算八个顶点到输入点的距离向量
        Vector3 d000 = new Vector3(xf, yf, zf);
        Vector3 d100 = new Vector3(xf - 1.0f, yf, zf);
        Vector3 d010 = new Vector3(xf, yf - 1.0f, zf);
        Vector3 d110 = new Vector3(xf - 1.0f, yf - 1.0f, zf);

        Vector3 d001 = new Vector3(xf, yf, zf - 1.0f);
        Vector3 d101 = new Vector3(xf - 1.0f, yf, zf - 1.0f);
        Vector3 d011 = new Vector3(xf, yf - 1.0f, zf - 1.0f);
        Vector3 d111 = new Vector3(xf - 1.0f, yf - 1.0f, zf - 1.0f);

        // 6. 计算点积 (梯度衰减)
        float dot000 = Vector3.Dot(g000, d000);
        float dot100 = Vector3.Dot(g100, d100);
        float dot010 = Vector3.Dot(g010, d010);
        float dot110 = Vector3.Dot(g110, d110);

        float dot001 = Vector3.Dot(g001, d001);
        float dot101 = Vector3.Dot(g101, d101);
        float dot011 = Vector3.Dot(g011, d011);
        float dot111 = Vector3.Dot(g111, d111);

        // 7. 三线性插值 (Trilinear Interpolation)
        // 第一步: 在 X 轴上插值 (形成4条边的中间值)
        float xInterp00 = Mathf.Lerp(dot000, dot100, u); // 底面近边
        float xInterp10 = Mathf.Lerp(dot010, dot110, u); // 底面远边
        float xInterp01 = Mathf.Lerp(dot001, dot101, u); // 顶面近边
        float xInterp11 = Mathf.Lerp(dot011, dot111, u); // 顶面远边

        // 第二步: 在 Y 轴上插值 (形成上下两个面的中间值)
        float yInterp0 = Mathf.Lerp(xInterp00, xInterp10, v); // 底面中心
        float yInterp1 = Mathf.Lerp(xInterp01, xInterp11, v); // 顶面中心

        // 第三步: 在 Z 轴上插值 (得到最终结果)
        float result = Mathf.Lerp(yInterp0, yInterp1, w);

        // 8. 归一化到 [0, 1]
        return (result + 1f) / 2f;
    }

    // 2D Simplex噪声
    public static float GetSimplexNoise2D(int seed, float x, float y, float z)
    {
        // 1. 坐标变换：从笛卡尔坐标系进入单纯形坐标系
        const float F2 = 0.3660254037844386f;
        // 将输入坐标 (x, y) 扭曲，得到 (i, j) 作为单纯形网格的整数坐标
        float s = (x + y) * F2;
        int i = Mathf.FloorToInt(x + s);
        int j = Mathf.FloorToInt(y + s);

        // 2. 常量定义
        const float G2 = 0.21132486540518713f;

        // 3. 计算在网格内的相对位置
        float t = (i + j) * G2;
        float X0 = i - t; // 网格点的原始坐标 X
        float Y0 = j - t; // 网格点的原始坐标 Y
        float x0 = x - X0;
        float y0 = y - Y0;

        // 4. 确定当前点属于哪种单纯形（三角形）
        int i1, j1;
        if (x0 > y0)
        {
            i1 = 1; j1 = 0;
        } // 主对角线以上 (0,0)->(1,0)->(1,1)
        else
        {
            i1 = 0; j1 = 1;
        } // 主对角线以下 (0,0)->(0,1)->(1,1)

        // 5. 计算另外两个顶点的偏移量
        float x1 = x0 - i1 + G2; // 相对于第二个顶点的坐标
        float y1 = y0 - j1 + G2;
        float x2 = x0 - 1.0f + 2.0f * G2; // 相对于第三个顶点的坐标
        float y2 = y0 - 1.0f + 2.0f * G2;

        // 6. 计算衰减函数 (贡献度)
        float n0 = 0f, n1 = 0f, n2 = 0f;

        // 点 (0,0) 的贡献
        float t0 = 0.5f - x0 * x0 - y0 * y0;
        if (t0 > 0)
        {
            // 计算梯度向量与距离向量的点积
            Vector2 g = GetNoiseGradient2D(i, j, seed);
            // 计算贡献值 (t^4)
            n0 = t0 * t0 * t0 * t0 * (g.x * x0 + g.y * y0);
        }

        // 点 (i1, j1) 的贡献
        float t1 = 0.5f - x1 * x1 - y1 * y1;
        if (t1 > 0)
        {
            Vector2 g = GetNoiseGradient2D(i + i1, j + j1, seed);
            n1 = t1 * t1 * t1 * t1 * (g.x * x1 + g.y * y1);
        }

        // 点 (1,1) 的贡献
        float t2 = 0.5f - x2 * x2 - y2 * y2;
        if (t2 > 0)
        {
            Vector2 g = GetNoiseGradient2D(i + 1, j + 1, seed);
            n2 = t2 * t2 * t2 * t2 * (g.x * x2 + g.y * y2);
        }

        // 7. 合并结果并归一化
        // 40 是一个经验缩放因子，确保结果在 [-1, 1] 附近
        float result = 40f * (n0 + n1 + n2);

        // 将结果从 [-1, 1] 映射到 [0, 1]
        return (result + 1f) / 2f;
    }

    // 3D Simplex噪声
    public static float GetSimplexNoise3D(int seed, float x, float y, float z)
    {
        // 1. 坐标扭曲 (Skewing)
        // F3 = 1/3 = 0.3333...
        const float F3 = 0.333333333f;
        // 将坐标扭曲，以便找到所在的网格单元
        float s = (x + y + z) * F3;
        int i = Mathf.FloorToInt(x + s);
        int j = Mathf.FloorToInt(y + s);
        int k = Mathf.FloorToInt(z + s);

        // 2. 反扭曲计算偏移量 (Unskewing)
        // G3 = 1/6 = 0.1666...
        const float G3 = 0.166666666f;
        float t = (i + j + k) * G3;
        // 计算网格点的原始坐标
        float X0 = i - t;
        float Y0 = j - t;
        float Z0 = k - t;
        // 计算当前点相对于网格点的偏移
        float x0 = x - X0;
        float y0 = y - Y0;
        float z0 = z - Z0;

        // 3. 确定四面体的类型 (通过排序确定位置)
        // 确定 x, y, z 哪个最大，哪个最小
        // rankx, ranky, rankz 的值将决定我们处于立方体的哪个四面体中
        int rankx, ranky, rankz;

        // 简单的排序逻辑
        if (x0 >= y0)
        {
            if (y0 >= z0) { rankx = 2; ranky = 1; rankz = 0; } // x>y>z
            else if (x0 >= z0) { rankx = 2; ranky = 0; rankz = 1; } // x>z>y
            else { rankx = 1; ranky = 0; rankz = 2; } // z>x>y
        }
        else
        {
            if (y0 < z0) { rankx = 0; ranky = 1; rankz = 2; } // x<y<z
            else if (x0 < z0) { rankx = 0; ranky = 2; rankz = 1; } // y>z>x
            else { rankx = 1; ranky = 2; rankz = 0; } // y>x>z
        }

        // 4. 计算相对于四个顶点的偏移量
        // 基于排序结果，计算相对于 4 个顶点的坐标 (x1,y1,z1), (x2,y2,z2), (x3,y3,z3)
        // G3 参与计算以补偿网格变形
        float x1 = x0 - rankx * G3 + G3;
        float y1 = y0 - ranky * G3 + G3;
        float z1 = z0 - rankz * G3 + G3;

        float x2 = x0 - rankx * G3 + 2.0f * G3;
        float y2 = y0 - ranky * G3 + 2.0f * G3;
        float z2 = z0 - rankz * G3 + 2.0f * G3;

        float x3 = x0 - 3.0f * G3;
        float y3 = y0 - 3.0f * G3;
        float z3 = z0 - 3.0f * G3;

        // 5. 计算四个顶点的贡献度
        float n0 = 0, n1 = 0, n2 = 0, n3 = 0;

        // 贡献度计算公式：(0.6 - dot(x, x))^4
        // 第一个顶点 (0, 0, 0)
        float t0 = 0.6f - x0 * x0 - y0 * y0 - z0 * z0;
        if (t0 > 0)
        {
            // 计算梯度点积
            Vector3 g = GetNoiseGradient3D(i, j, k, seed);
            float dotProduct = g.x * x0 + g.y * y0 + g.z * z0;
            n0 = t0 * t0 * t0 * t0 * dotProduct;
        }

        // 第二个顶点 (i1, j1, k1)
        float t1 = 0.6f - x1 * x1 - y1 * y1 - z1 * z1;
        if (t1 > 0)
        {
            Vector3 g = GetNoiseGradient3D(i + (rankx >= 1 ? 1 : 0),
                                          j + (ranky >= 1 ? 1 : 0),
                                          k + (rankz >= 1 ? 1 : 0), seed);
            float dotProduct = g.x * x1 + g.y * y1 + g.z * z1;
            n1 = t1 * t1 * t1 * t1 * dotProduct;
        }

        // 第三个顶点 (i2, j2, k2)
        float t2 = 0.6f - x2 * x2 - y2 * y2 - z2 * z2;
        if (t2 > 0)
        {
            Vector3 g = GetNoiseGradient3D(i + (rankx >= 2 ? 1 : 0),
                                          j + (ranky >= 2 ? 1 : 0),
                                          k + (rankz >= 2 ? 1 : 0), seed);
            float dotProduct = g.x * x2 + g.y * y2 + g.z * z2;
            n2 = t2 * t2 * t2 * t2 * dotProduct;
        }

        // 第四个顶点 (1, 1, 1)
        float t3 = 0.6f - x3 * x3 - y3 * y3 - z3 * z3;
        if (t3 > 0)
        {
            Vector3 g = GetNoiseGradient3D(i + 1, j + 1, k + 1, seed);
            float dotProduct = g.x * x3 + g.y * y3 + g.z * z3;
            n3 = t3 * t3 * t3 * t3 * dotProduct;
        }

        // 6. 合并结果并归一化
        // 32 是 3D Simplex 的经验缩放因子
        float result = 32.0f * (n0 + n1 + n2 + n3);

        // 映射到 [0, 1]
        return (result + 1.0f) / 2.0f;
    }

    // 2D Value噪声
    public static float GetValueNoise2D(int seed, float x, float y, float z)
    {
        // 1. 找到左下角的网格坐标
        // 这将把平面划分为整数格子
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        // 右上角坐标
        int x1 = x0 + 1;
        int y1 = y0 + 1;

        // 2. 计算当前点在格子内的相对位置 (0~1)
        float fx = x - x0;
        float fy = y - y0;

        float v00 = Hash2D(x0, y0, seed); // 左下
        float v10 = Hash2D(x1, y0, seed); // 右下
        float v01 = Hash2D(x0, y1, seed); // 左上
        float v11 = Hash2D(x1, y1, seed); // 右上

        // 4. 双线性插值 (Bilinear Interpolation)
        float u = Fade(fx);
        float v = Fade(fy);

        // X轴插值
        float ix0 = Mathf.Lerp(v00, v10, u);
        float ix1 = Mathf.Lerp(v01, v11, u);
        // Y轴插值
        float value = Mathf.Lerp(ix0, ix1, v);

        // 5. Value Noise 的值域通常是 [0, 1]，不需要像 Perlin 那样映射
        // 但为了统一接口，确保安全
        return Mathf.Clamp01(value);
    }

    // 3D Value噪声
    public static float GetValueNoise3D(int seed, float x, float y, float z)
    {
        // 1. 找到立方体网格的整数坐标
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int z0 = Mathf.FloorToInt(z);

        int x1 = x0 + 1;
        int y1 = y0 + 1;
        int z1 = z0 + 1;

        // 2. 计算点在立方体内的相对位置 (0~1)
        float fx = x - x0;
        float fy = y - y0;
        float fz = z - z0;

        // 4. 获取立方体 8 个顶点的随机值
        float v000 = Hash3D(x0, y0, z0, seed); // 后下左
        float v100 = Hash3D(x1, y0, z0, seed); // 后下右
        float v010 = Hash3D(x0, y1, z0, seed); // 后上左
        float v110 = Hash3D(x1, y1, z0, seed); // 后上右

        float v001 = Hash3D(x0, y0, z1, seed); // 前下左
        float v101 = Hash3D(x1, y0, z1, seed); // 前下右
        float v011 = Hash3D(x0, y1, z1, seed); // 前上左
        float v111 = Hash3D(x1, y1, z1, seed); // 前上右

        // 5. 三线性插值 (Trilinear Interpolation)
        float u = Fade(fx);
        float v = Fade(fy);
        float w = Fade(fz);

        // 第一步：X 轴插值 (计算 4 条边的值)
        float ix00 = Mathf.Lerp(v000, v100, u); // 后下边
        float ix10 = Mathf.Lerp(v010, v110, u); // 后上边
        float ix01 = Mathf.Lerp(v001, v101, u); // 前下边
        float ix11 = Mathf.Lerp(v011, v111, u); // 前上边

        // 第二步：Y 轴插值 (计算 2 个面的值)
        float iy0 = Mathf.Lerp(ix00, ix10, v); // 后面
        float iy1 = Mathf.Lerp(ix01, ix11, v); // 前面

        // 第三步：Z 轴插值 (计算最终立方体内部的值)
        float value = Mathf.Lerp(iy0, iy1, w);

        return Mathf.Clamp01(value);
    }

    // 2D Voronoi噪声
    public static float GetVoronoiNoise2D(int seed, float x, float y, float z)
    {
        // 1. 找到当前点所在的网格单元
        // Voronoi 噪声通常也是基于网格生成的，每个格子里放一个种子点
        int cellX = Mathf.FloorToInt(x);
        int cellY = Mathf.FloorToInt(y);

        // 2. 计算点在格子内的相对位置 (0~1)
        float localX = x - cellX;
        float localY = y - cellY;

        // 3. 初始化最小距离为无穷大
        float minDistance = float.MaxValue;

        // 4. 遍历 3x3 的邻域格子
        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                // 获取相邻格子的坐标
                int neighborCellX = cellX + i;
                int neighborCellY = cellY + j;

                // 5. 为每个格子生成一个伪随机的种子点位置
                float pointX = Hash2D(neighborCellX, neighborCellY, seed);
                float pointY = Hash2D(neighborCellX, neighborCellY, seed + 1);

                // 6. 计算当前点到这个种子点的距离
                float distanceX = (neighborCellX + pointX) - (cellX + localX);
                float distanceY = (neighborCellY + pointY) - (cellY + localY);

                // 计算欧几里得距离的平方 (为了性能，先不开根号)
                float distanceSqr = distanceX * distanceX + distanceY * distanceY;

                // 7. 更新最小距离
                if (distanceSqr < minDistance)
                {
                    minDistance = distanceSqr;
                }
            }
        }
        // 8. 结果处理
        // 开根号得到真实的距离
        float distance = Mathf.Sqrt(minDistance);
        // 归一化：Voronoi 的距离通常在 0 到 ~1.5 之间，除以 1.5 映射到 0-1
        return Mathf.Clamp01(distance);
    }

    // 3D Voronoi噪声
    public static float GetVoronoiNoise3D(int seed, float x, float y, float z)
    {
        // 1. 找到当前点所在的网格单元 (3D)
        int cellX = Mathf.FloorToInt(x);
        int cellY = Mathf.FloorToInt(y);
        int cellZ = Mathf.FloorToInt(z);

        // 2. 计算点在格子内的相对位置 (0~1)
        float localX = x - cellX;
        float localY = y - cellY;
        float localZ = z - cellZ;

        // 3. 初始化最小距离为无穷大
        float minDistance = float.MaxValue;

        // 4. 遍历 3x3x3 的邻域格子 (共27个)
        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                for (int k = -1; k <= 1; k++)
                {
                    // 获取相邻格子的坐标
                    int neighborCellX = cellX + i;
                    int neighborCellY = cellY + j;
                    int neighborCellZ = cellZ + k;

                    // 5. 为每个格子生成一个伪随机的种子点位置
                    float pointX = Hash3D(neighborCellX, neighborCellY, neighborCellZ, seed);
                    float pointY = Hash3D(neighborCellX, neighborCellY, neighborCellZ, seed + 1);
                    float pointZ = Hash3D(neighborCellX, neighborCellY, neighborCellZ, seed + 2);

                    // 6. 计算当前点到这个种子点的距离 (3D距离)
                    float distanceX = (neighborCellX + pointX) - (cellX + localX);
                    float distanceY = (neighborCellY + pointY) - (cellY + localY);
                    float distanceZ = (neighborCellZ + pointZ) - (cellZ + localZ);

                    // 计算欧几里得距离的平方
                    float distanceSqr = distanceX * distanceX + distanceY * distanceY + distanceZ * distanceZ;

                    // 7. 更新最小距离
                    if (distanceSqr < minDistance)
                    {
                        minDistance = distanceSqr;
                    }
                }
            }
        }
        // 8. 结果处理
        float distance = Mathf.Sqrt(minDistance);
        // 归一化并返回
        return Mathf.Clamp01(distance);
    }
    // 2D FBM 噪声
    public static float GetFBMNoise2D(int seed, float x, float y, float z)
    {
        //读取参数 (从 NoiseData 中获取)
        int octaves = NoiseData.Octaves;
        float lacunarity = NoiseData.Lacunarity;
        float persistence = NoiseData.Persistence;
        float amplitude = 1.0f;       // 当前层的振幅（强度）
        float frequency = 1.0f;       // 当前层的频率（采样缩放比例）
        float noiseValue = 0.0f;      // 最终累加的噪声值
        float normalization = 0.0f;   // 归一化因子，用于将结果保持在 [0, 1] 范围内

        // 循环叠加
        for (int i = 0; i < octaves; i++)
        {
            // A. 采样基础噪声
            float sample = GetPerlinNoise2D(seed, x * frequency, y * frequency, 0f);

            // B. 累加贡献值
            noiseValue += sample * amplitude;

            // C. 累加归一化权重
            normalization += amplitude;

            // D. 更新下一层参数
            frequency *= lacunarity;
            amplitude *= persistence;
        }

        // 4. 归一化处理
        if (normalization > 0)
        {
            noiseValue /= normalization;
        }

        // 5. 范围修正
        return Mathf.Clamp01(noiseValue);
    }
    // 对3D FBM 专项多线程优化
    private struct FBMJobData
    {
        public int seed;
        public float x, y, z;
        public int octaves;
        public float lacunarity;
        public float persistence;

        public NativeArray<float> result;

    }
    // 2. 定义私有的 Job 结构体
    [BurstCompile]
    private struct FBMJob3D : IJob
    {
        public FBMJobData data;

        public void Execute()
        {
            float amplitude = 1.0f;
            float frequency = 1.0f;
            float noiseValue = 0.0f;
            float normalization = 0.0f;

            for (int i = 0; i < data.octaves; i++)
            {
                float sample = FastPerlin3D(data.seed, data.x * frequency, data.y * frequency, data.z * frequency);

                noiseValue += sample * amplitude;
                normalization += amplitude;
                frequency *= data.lacunarity;
                amplitude *= data.persistence;
            }

            if (normalization > 0)
                noiseValue /= normalization;

            // 写入结果
            data.result[0] = Mathf.Clamp01(noiseValue);
        }

        private float FastPerlin3D(int seed, float x, float y, float z)
        {
            int x0 = (int)(x + (x > 0 ? 0 : -1)); // 简单的 Floor 模拟，或者使用 Math.Floor
                                                  // 更严谨的写法（兼容负数）：
            x0 = (int)Math.Floor(x);
            int x1 = x0 + 1;

            int y0 = (int)Math.Floor(y);
            int y1 = y0 + 1;

            int z0 = (int)Math.Floor(z);
            int z1 = z0 + 1;

            float xf = (float)(x - x0);
            float yf = (float)(y - y0);
            float zf = (float)(z - z0);

            float u = xf * xf * xf * (xf * (xf * 6f - 15f) + 10f);
            float v = yf * yf * yf * (yf * (yf * 6f - 15f) + 10f);
            float w = zf * zf * zf * (zf * (zf * 6f - 15f) + 10f);

            float Dot(int ix, int iy, int iz, float dx, float dy, float dz)
            {
                int n = seed + ix * 1619 + iy * 31337 + iz * 6971;
                n = (n << 13) ^ n;

                int k1 = n * (n * n * 15731 + 789221) + 1376312589;
                int k2 = n * (n * n * 19263 + 840129) + 1024124197;

                float u = (k1 & 0x7fffffff) / (float)0x80000000;
                float v = (k2 & 0x7fffffff) / (float)0x80000000;

                float theta = u * (float)Math.PI * 2f;
                float phi = v * (float)Math.PI;

                float sinPhi = (float)Math.Sin(phi);
                float gx = sinPhi * (float)Math.Cos(theta);
                float gy = sinPhi * (float)Math.Sin(theta);
                float gz = (float)Math.Cos(phi);

                return gx * dx + gy * dy + gz * dz;
            }
            float dot000 = Dot(x0, y0, z0, xf, yf, zf);
            float dot100 = Dot(x1, y0, z0, xf - 1f, yf, zf);
            float dot010 = Dot(x0, y1, z0, xf, yf - 1f, zf);
            float dot110 = Dot(x1, y1, z0, xf - 1f, yf - 1f, zf);

            float dot001 = Dot(x0, y0, z1, xf, yf, zf - 1f);
            float dot101 = Dot(x1, y0, z1, xf - 1f, yf, zf - 1f);
            float dot011 = Dot(x0, y1, z1, xf, yf - 1f, zf - 1f);
            float dot111 = Dot(x1, y1, z1, xf - 1f, yf - 1f, zf - 1f);

            float x00 = dot000 + u * (dot100 - dot000);
            float x10 = dot010 + u * (dot110 - dot010);
            float x01 = dot001 + u * (dot101 - dot001);
            float x11 = dot011 + u * (dot111 - dot011);

            float y_0 = x00 + v * (x10 - x00);
            float y_1 = x01 + v * (x11 - x01);

            float result = y_0 + w * (y_1 - y_0);
            return (result + 1f) * 0.5f; // 0.5f 代替除法，效率更高
        }
    }
    // 3. 优化后的 GetFBMNoise3D 函数
    public static float GetFBMNoise3D(int seed, float x, float y, float z)
    {
        // 1. 读取参数
        int octaves = NoiseData.Octaves;
        float lacunarity = NoiseData.Lacunarity;
        float persistence = NoiseData.Persistence;

        // 2. ✅ 关键修改：使用 Allocator.TempJob
        // 这种分配器与 Job 绑定，会在 Job 完成后自动释放，且线程安全
        NativeArray<float> resultArray = new NativeArray<float>(1, Allocator.TempJob);

        // 3. 初始化 Job 数据 (手动赋值字段)
        FBMJobData jobData = new FBMJobData();
        jobData.seed = seed;
        jobData.x = x;
        jobData.y = y;
        jobData.z = z;
        jobData.octaves = octaves;
        jobData.lacunarity = lacunarity;
        jobData.persistence = persistence;
        jobData.result = resultArray; // 传入数组引用

        // 4. 创建并执行 Job
        FBMJob3D job = new FBMJob3D { data = jobData };

        // 5. ✅ 关键修改：使用 Schedule 并配合 Complete
        // 虽然你是同步逻辑，但必须通过 JobHandle.Complete() 来确保执行完毕并释放内存
        JobHandle handle = job.Schedule();
        handle.Complete(); // 这行代码会阻塞直到 Job 执行完，并自动释放 TempJob 内存

        // 6. 读取结果
        float result = resultArray[0];

        // ✅ 注意：使用 Allocator.TempJob 时，不需要手动调用 resultArray.Dispose()
        // 内存会在 handle.Complete() 时自动回收
        // 如果你手动 Dispose，反而会导致错误

        return result;
    }
}