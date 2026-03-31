using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class NoiseCreate : EditorWindow
{
    // 噪声维度
    public enum NoiseDimension { TwoD, ThreeD }
    public NoiseDimension selectedDimension = NoiseDimension.TwoD;

    // 噪声类型
    public enum NoiseType { Perlin, Simplex, Value, Voronoi, FBM_Perlin }
    public NoiseType selectedNoiseType = NoiseType.Perlin;

    // 基础参数
    public float scale = 0.01f;
    public int seed = 0;

    // 2D专用
    public int width = 512;
    public int height = 512;

    // 3D专用
    public int depth = 512;
    //FBM噪声专用
    public int octaves = 6;
    public float lacunarity = 2.0f;
    public float persistence = 0.5f;

    Dictionary<(NoiseDimension, NoiseType), Func<Texture2D>> UseID2D = new Dictionary<(NoiseDimension, NoiseType), Func<Texture2D>>();
    Dictionary<(NoiseDimension, NoiseType), Func<Texture3D>> UseID3D = new Dictionary<(NoiseDimension, NoiseType), Func<Texture3D>>();


    [MenuItem("Tools/Noise")]
    public static void Noise()
    {
        NoiseCreate.CreateInstance<NoiseCreate>().Show();
    }

    public void OnEnable()
    {
        UseID2D.Add((NoiseDimension.TwoD, NoiseType.Perlin), NoiseGenerator.GenerateTexture2D);
        UseID2D.Add((NoiseDimension.TwoD, NoiseType.Simplex), NoiseGenerator.GenerateTexture2D);
        UseID2D.Add((NoiseDimension.TwoD, NoiseType.Value), NoiseGenerator.GenerateTexture2D);
        UseID2D.Add((NoiseDimension.TwoD, NoiseType.Voronoi), NoiseGenerator.GenerateTexture2D);
        UseID2D.Add((NoiseDimension.TwoD, NoiseType.FBM_Perlin), NoiseGenerator.GenerateTexture2D);
        UseID3D.Add((NoiseDimension.ThreeD, NoiseType.Perlin), NoiseGenerator.GenerateTexture3D);
        UseID3D.Add((NoiseDimension.ThreeD, NoiseType.Simplex), NoiseGenerator.GenerateTexture3D);
        UseID3D.Add((NoiseDimension.ThreeD, NoiseType.Value), NoiseGenerator.GenerateTexture3D);
        UseID3D.Add((NoiseDimension.ThreeD, NoiseType.Voronoi), NoiseGenerator.GenerateTexture3D);
        UseID3D.Add((NoiseDimension.ThreeD, NoiseType.FBM_Perlin), NoiseGenerator.GenerateTexture3D);
    }

    public void OnGUI()
    {
        // 窗口标题
        GUILayout.Label("噪声生成器", EditorStyles.boldLabel);
        EditorGUILayout.Space(10);

        // 1. 选择2D或3D
        EditorGUILayout.LabelField("噪声维度", EditorStyles.boldLabel);
        selectedDimension = (NoiseDimension)EditorGUILayout.EnumPopup("维度", selectedDimension);

        EditorGUILayout.Space(5);

        // 2. 选择噪声类型
        EditorGUILayout.LabelField("噪声类型", EditorStyles.boldLabel);
        selectedNoiseType = (NoiseType)EditorGUILayout.EnumPopup("类型", selectedNoiseType);

        EditorGUILayout.Space(5);

        // 3. FBM 专用参数 (动态显示)
        if (selectedNoiseType == NoiseType.FBM_Perlin)
        {
            EditorGUILayout.LabelField("FBM_Perlin 分形参数", EditorStyles.boldLabel);

            // Octaves: 层级数
            octaves = EditorGUILayout.IntSlider("Octaves (层级)", octaves, 1, 10);

            // Lacunarity: 频率间隙
            lacunarity = EditorGUILayout.Slider("Lacunarity (频率)", lacunarity, 1.0f, 4.0f);

            // Persistence: 振幅持久度
            persistence = EditorGUILayout.Slider("Persistence (强度)", persistence, 0.01f, 1.0f);

            EditorGUILayout.Space(5);
        }

        // 4. 通用参数
        EditorGUILayout.LabelField("噪声参数", EditorStyles.boldLabel);
        scale = EditorGUILayout.Slider("缩放", scale, 0.01f, 0.1f);
        seed = EditorGUILayout.IntField("随机种子", seed);

        EditorGUILayout.Space(5);

        // 5. 维度相关参数
        EditorGUILayout.LabelField("输出设置", EditorStyles.boldLabel);

        if (selectedDimension == NoiseDimension.TwoD)
        {
            width = EditorGUILayout.IntField("宽度", width);
            height = EditorGUILayout.IntField("高度", height);
        }
        else
        {
            width = EditorGUILayout.IntField("宽度", width);
            height = EditorGUILayout.IntField("高度", height);
            depth = EditorGUILayout.IntField("深度", depth);

            EditorGUILayout.Space(3);
        }

        EditorGUILayout.Space(10);

        // 操作按钮区域
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("生成", GUILayout.Width(80)))
        {
            // 1. 写入静态类参数
            NoiseData.Scale = this.scale;
            NoiseData.Seed = this.seed;
            NoiseData.Width = this.width;
            NoiseData.Height = this.height;
            NoiseData.Depth = this.depth;
            NoiseData.SelectedDimension = this.selectedDimension;
            NoiseData.SelectedNoiseType = this.selectedNoiseType;

            // 2. 根据维度选择生成逻辑
            // 定义保存路径
            string folderPath = "Assets/GeneratedTextures";
            string baseFileName = "Noise";

            if (!System.IO.Directory.Exists(folderPath))
            {
                System.IO.Directory.CreateDirectory(folderPath);
            }

            // --- 核心逻辑：分支处理 (关键修改点) ---
            if (selectedDimension == NoiseDimension.TwoD)
            {
                // --- 2D 逻辑：调用 UseID2D ---
                Texture2D generatedTex2D = UseID2D[(selectedDimension, selectedNoiseType)]();

                // 保存 PNG 逻辑
                string[] files = System.IO.Directory.GetFiles(folderPath, "*.png");
                int maxNumber = 0;
                foreach (string file in files)
                {
                    string existingFileName = System.IO.Path.GetFileNameWithoutExtension(file);
                    if (existingFileName.Length > baseFileName.Length)
                    {
                        string numStr = existingFileName.Substring(baseFileName.Length);
                        if (int.TryParse(numStr, out int num))
                        {
                            if (num > maxNumber)
                            {
                                maxNumber = num;
                            }
                        }
                    }
                }

                int nextNumber = maxNumber + 1;
                string fileName = $"{baseFileName}{nextNumber:D3}.png";
                string fullPath = System.IO.Path.Combine(folderPath, fileName);

                byte[] bytes = generatedTex2D.EncodeToPNG();
                System.IO.File.WriteAllBytes(fullPath, bytes);
            }
            else // NoiseDimension.ThreeD
            {
                // --- 3D 逻辑：调用 UseID3D ---
                Texture3D generatedTex3D = UseID3D[(selectedDimension, selectedNoiseType)]();

                // 3D 纹理不能直接保存为 PNG，必须保存为 Unity Asset (.asset)
                // 生成唯一的 Asset 文件名
                string assetName = $"{baseFileName}{DateTime.Now.ToString("HHmmss")}.asset"; // 使用时间避免重名
                string assetPath = System.IO.Path.Combine(folderPath, assetName);

                // 检查并删除旧的同名资源（可选）
                if (System.IO.File.Exists(assetPath))
                {
                    AssetDatabase.DeleteAsset(assetPath);
                }

                // 保存为 Asset
                AssetDatabase.CreateAsset(generatedTex3D, assetPath);
            }

            // 刷新资源
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
#endif
        }


        EditorGUILayout.EndHorizontal();

    }
}