﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;

public class BakingScript : MonoBehaviour
{
    public bool WriteOutput;

    public bool DisplayLongevity;

    public Material[] Mats;

    [Range(0, 1)]
    public float HeatBurst;
    [Range(.9f, 1)]
    public float HeatDecay;

    [Range(0f, 1)]
    public float HistoryShift;

    [Range(0, 5)]
    public float HeatArc;

    public ComputeShader Compute;

    public Text Progressbar;

    private int index;
    private const int ImageResolution = MainViewerScript.ImageResolution;
    private const int FullResolution = MainViewerScript.ImageResolution * MainViewerScript.ImageResolution;


    private IRawDataSource rawDataSource;

    private int computeKernel;
    private int occlusionKernel;

    private ComputeBuffer dataBuffer;
    private int dataBufferStride = sizeof(float) * 2 //SourcePosition
        + sizeof(float) // Heat
        + sizeof(uint)  // Color
        + sizeof(float) * 32; // ColorHistory

    private ComputeBuffer gridBuffer;
    private int gridBufferStride = sizeof(float) * 2;
    
    private const int DispatchGroupSize = 128;
    
    private byte[] outputPngData;

    private Texture2D outputVessel;
    private RenderTexture intermediateRenderTexture;
    private RenderTexture outputRenderTexture;

    private Rect kRec = new Rect(0, 0, ImageResolution, ImageResolution);

    struct ParticleData
    {
        public Vector2 SourcePosition;
        public float Heat;
        public uint Color;
        public float ColorHistory0;
        public float ColorHistory1;
        public float ColorHistory2;
        public float ColorHistory3;
        public float ColorHistory4;
        public float ColorHistory5;
        public float ColorHistory6;
        public float ColorHistory7;
        public float ColorHistory8;
        public float ColorHistory9;
        public float ColorHistory10;
        public float ColorHistory11;
        public float ColorHistory12;
        public float ColorHistory13;
        public float ColorHistory14;
        public float ColorHistory15;
        public float ColorHistory16;
        public float ColorHistory17;
        public float ColorHistory18;
        public float ColorHistory19;
        public float ColorHistory20;
        public float ColorHistory21;
        public float ColorHistory22;
        public float ColorHistory23;
        public float ColorHistory24;
        public float ColorHistory25;
        public float ColorHistory26;
        public float ColorHistory27;
        public float ColorHistory28;
        public float ColorHistory29;
        public float ColorHistory30;
        public float ColorHistory31;
    }

    void Start()
    {
        computeKernel = Compute.FindKernel("CSMain");
        occlusionKernel = Compute.FindKernel("OcclusionCompute");
        InitializeDataBuffer();
        InitializeGridBuffer();

        rawDataSource = new GZipSource(); // new ScreenshotSource();

        outputVessel = new Texture2D(ImageResolution, ImageResolution, TextureFormat.ARGB32, false);

        intermediateRenderTexture = new RenderTexture(ImageResolution, ImageResolution, 0, RenderTextureFormat.ARGB32);
        intermediateRenderTexture.filterMode = FilterMode.Point;
        intermediateRenderTexture.wrapMode = TextureWrapMode.Clamp;
        intermediateRenderTexture.enableRandomWrite = true;
        intermediateRenderTexture.Create();

        outputRenderTexture = GetRenderTexture();
    }

    private RenderTexture GetRenderTexture()
    {
        RenderTexture ret = new RenderTexture(ImageResolution, ImageResolution, 0, RenderTextureFormat.ARGB32);
        ret.filterMode = FilterMode.Point;
        ret.wrapMode = TextureWrapMode.Clamp;
        ret.enableRandomWrite = true;
        ret.Create();
        return ret;
    }

    private void InitializeGridBuffer()
    {
        gridBuffer = new ComputeBuffer(FullResolution, gridBufferStride);
        Vector2[] data = new Vector2[FullResolution];
        for (int i = 0; i < ImageResolution; i++)
        {
            for (int j = 0; j < ImageResolution; j++)
            {
                Vector2 position = new Vector2((float)i / ImageResolution, (float)j / ImageResolution);
                data[i * ImageResolution + j] = position;
            }
        }
        gridBuffer.SetData(data);
    }

    private void InitializeDataBuffer()
    {
        dataBuffer = new ComputeBuffer(FullResolution, dataBufferStride);
        ParticleData[] data = new ParticleData[FullResolution];
        for (int i = 0; i < ImageResolution; i++)
        {
            for (int j = 0; j < ImageResolution; j++)
            {
                ParticleData datum = new ParticleData();
                datum.SourcePosition = new Vector2(i, j);
                data[i * ImageResolution + j] = datum;
            }
        }
        dataBuffer.SetData(data);
    }

    private void Update()
    {
        if(rawDataSource.CurrentStepIndex == rawDataSource.TotalSteps)
        {
            Progressbar.text = "Processing Complete";
            return;
        }
        index++;
        Progressbar.text = GetProgressText() ;

        rawDataSource.SetNextStep();
        Compute.SetBuffer(computeKernel, "_SourceDataBuffer", rawDataSource.PixelIndexValuesBuffer);
        
        int groupSize = Mathf.CeilToInt((float)FullResolution / DispatchGroupSize);

        Compute.SetFloat("_FrameIndex", index);
        Compute.SetFloat("_HeatBurst", HeatBurst);
        Compute.SetFloat("_HeatDecay", HeatDecay);
        Compute.SetBuffer(computeKernel, "_DataBuffer", dataBuffer);
        Compute.SetTexture(computeKernel, "OutputTexture", intermediateRenderTexture);
        Compute.Dispatch(computeKernel, groupSize, 1, 1);

        Compute.SetBuffer(occlusionKernel, "_DataBuffer", dataBuffer);
        Compute.SetTexture(occlusionKernel, "OcclusionInputTexture", intermediateRenderTexture);
        Compute.SetTexture(occlusionKernel, "OutputTexture", outputRenderTexture);
        Compute.Dispatch(occlusionKernel, groupSize, 1, 1);
        
        if(WriteOutput)
        {
            string outputPath = Path.Combine(MainViewerScript.OutputFolder, index.ToString("D8") + ".png");
            RenderTexture.active = outputRenderTexture;
            outputVessel.ReadPixels(kRec, 0, 0);
            RenderTexture.active = null;
            outputPngData = outputVessel.EncodeToPNG();
            File.WriteAllBytes(outputPath, outputPngData);
        }

        foreach (Material mat in Mats)
        {
            mat.SetFloat("_LongevityHeightAlpha", DisplayLongevity ? 1 : 0);
            mat.SetFloat("_HeatHeightAlpha", DisplayLongevity ? 0 : 1);
            mat.SetFloat("_LongevityAlpha", DisplayLongevity ? 1 : 0);
            mat.SetFloat("_HeatAlpha", 0);
            mat.SetTexture("_MainTex", outputRenderTexture);
        }
    }

    private string GetProgressText()
    {
        int diffIndex = Math.Max(rawDataSource.CurrentStepIndex, 1);// Prevent divide by zero later on
        float prog = (float)diffIndex / rawDataSource.TotalSteps;
        int percent = (int)(100 * prog);
        string ret = rawDataSource.CurrentStepIndex + " of " + rawDataSource.TotalSteps
            + "\n" + percent + "% complete";
        if (!WriteOutput)
            ret += ". Writing Output = FALSE";
        return ret;
    }

    private void OnDestroy()
    {
        rawDataSource.Dispose();
        gridBuffer.Dispose();
        dataBuffer.Dispose();
        intermediateRenderTexture.Release();
        outputRenderTexture.Release();
    }
}

