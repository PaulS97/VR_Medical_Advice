using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Unity.Sentis;
using System.IO;
using Newtonsoft.Json;
using System.Text;
using System;
using UnityEngine.Networking;

/*
 *              Whisper Inference Code
 *              ======================
 *  
 *  Put this script on the Main Camera
 *  
 *  In Assets/StreamingAssets put:
 *  
 *  AudioDecoder_Tiny.sentis
 *  AudioEncoder_Tiny.sentis
 *  LogMelSepctro.sentis
 *  vocab.json
 * 
 *  Drag a 30s 16khz mono uncompressed audioclip into the audioClip field. 
 * 
 *  Install package com.unity.nuget.newtonsoft-json from packagemanger
 *  Install package com.unity.sentis
 * 
 */


public class RunWhisper : MonoBehaviour
{
    
    [SerializeField] ModelAsset encoderModel, decoderModel, spectroModel;
    
    IWorker decoderEngine, encoderEngine, spectroEngine;

    const BackendType backend = BackendType.GPUCompute;

    // Link your audioclip here. Format must be 16Hz mono non-compressed.
    public AudioClip audioClip;

    public SpeechRecognitionController speechRecognitionController;

    // This is how many tokens you want. It can be adjusted.
    const int maxTokens = 100;

    //Special tokens
    const int END_OF_TEXT = 50257;
    const int START_OF_TRANSCRIPT = 50258;
    const int ENGLISH = 50259;
    const int TRANSCRIBE = 50359;
    const int START_TIME = 50364;

    Ops ops;
    ITensorAllocator allocator;

    int numSamples;
    float[] data;
    string[] tokens;

    int currentToken = 0;
    int[] outputTokens = new int[maxTokens];

    // Used for special character decoding
    int[] whiteSpaceCharacters = new int[256];

    TensorFloat encodedAudio;

    bool transcribe = false;
    string outputString = "";

    // Maximum size of audioClip (30s at 16kHz)
    const int maxSamples = 30 * 16000;

    void Start()
    {
        Debug.Log("Debug_log: 🔄 Start() called in RunWhisper 1.");

        allocator = new TensorCachingAllocator();
        Debug.Log("Debug_log: extra 1.");

        ops = WorkerFactory.CreateOps(backend, allocator);
        Debug.Log("Debug_log: extra 2.");


        SetupWhiteSpaceShifts();
        Debug.Log("Debug_log: extra 3.");

        StartCoroutine(LoadVocab());


        Debug.Log("Debug_log: extra 4.");

        // Step-by-step model loading
        Debug.Log("Debug_log: 📥 Attempting to load encoder model...");
        Model encoder = ModelLoader.Load(encoderModel);
        Debug.Log(encoder != null ? "Debug_log: ✅ Encoder model loaded successfully." : "Debug_log: ❌ Encoder model FAILED to load!");

        Debug.Log("Debug_log: 📥 Attempting to load decoder model...");
        Model decoder = ModelLoader.Load(decoderModel);
        Debug.Log(decoder != null ? "Debug_log: ✅ Decoder model loaded successfully." : "Debug_log: ❌ Decoder model FAILED to load!");

        Debug.Log("Debug_log: 📥 Attempting to load spectrogram model...");
        Model spectro = ModelLoader.Load(spectroModel);
        Debug.Log(spectro != null ? "Debug_log: ✅ Spectrogram model loaded successfully." : "Debug_log: ❌ Spectrogram model FAILED to load!");

        // Assign Workers
        decoderEngine = WorkerFactory.CreateWorker(backend, decoder);
        encoderEngine = WorkerFactory.CreateWorker(backend, encoder);
        spectroEngine = WorkerFactory.CreateWorker(backend, spectro);

        Debug.Log(spectroEngine != null ? "Debug_log: ✅ Spectro engine created successfully." : "Debug_log: ❌ Spectro engine is STILL NULL!");

        Debug.Log("Debug_log: ✅ RunWhisper Start() complete.");
    }

    IEnumerator LoadVocab()
    {
        string path = Application.streamingAssetsPath + "/vocab.json";
        Debug.Log($"Debug_log: 📂 Attempting to load vocab.json from: {path}");

        using (UnityWebRequest request = UnityWebRequest.Get(path))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Debug_log: ❌ vocab.json NOT FOUND! " + request.error);
            }
            else
            {
                Debug.Log("Debug_log: ✅ vocab.json successfully loaded!");
                string jsonText = request.downloadHandler.text;

                var vocab = JsonConvert.DeserializeObject<Dictionary<string, int>>(jsonText);
                tokens = new string[vocab.Count];

                foreach (var item in vocab)
                {
                    tokens[item.Value] = item.Key;
                }

                Debug.Log("Debug_log: ✅ Vocab processing complete!");
            }
        }
    }


    private bool isTranscribing = false; // New flag

    public void Transcribe()
    {
        if (isTranscribing)
        {
            Debug.Log("Debug_log: ⚠️ Transcription already in progress. Skipping.");
            return;
        }

        isTranscribing = true; // Mark transcription as started
        Debug.Log("Debug_log: 📝 Transcription function started!");

        // Reset output tokens
        outputTokens[0] = START_OF_TRANSCRIPT;
        outputTokens[1] = ENGLISH;
        outputTokens[2] = TRANSCRIBE;
        outputTokens[3] = START_TIME;
        currentToken = 3;

        // Reset output string (transcript)
        outputString = "";

        Debug.Log($"Debug_log: 📝 Transcribe() called. AudioClip: {audioClip}");
        if (audioClip == null)
        {
            Debug.LogError("Debug_log: ❌ ERROR: AudioClip is NULL in RunWhisper.Transcribe()!");
            isTranscribing = false;
            return;
        }

        LoadAudio();
        EncodeAudio();
        transcribe = true;
        Debug.Log("Debug_log: ✅ AudioClip assigned! Sending to Whisper...");
    }



    void LoadAudio()
    {
        if(audioClip.frequency != 16000)
        {
            Debug.Log($"The audio clip should have frequency 16kHz. It has frequency {audioClip.frequency / 1000f}kHz");
            return;
        }

        numSamples = audioClip.samples;

        if (numSamples > maxSamples)
        {
            Debug.Log($"The AudioClip is too long. It must be less than 30 seconds. This clip is {numSamples/ audioClip.frequency} seconds.");
            return;
        }

        data = new float[numSamples];
        audioClip.GetData(data, 0);

        Debug.Log("Debug_log: 🎧 Loading audio into Whisper...");
        if (audioClip == null)
        {
            Debug.LogError("Debug_log: ❌ ERROR: AudioClip is NULL in LoadAudio()!");
            return;
        }

        float[] audioData = new float[audioClip.samples * audioClip.channels];
        audioClip.GetData(audioData, 0);
        Debug.Log($"Debug_log: ✅ Audio loaded! Total samples: {audioData.Length}");
    }


    void GetTokens()
    {
        string vocabPath = Path.Combine(Application.streamingAssetsPath, "vocab.json");
        Debug.Log($"Debug_log: 📂 Attempting to load vocab.json from: {vocabPath}");

        if (!File.Exists(vocabPath))
        {
            Debug.LogError("Debug_log: ❌ vocab.json NOT FOUND!");
            return;
        }

        var jsonText = File.ReadAllText(vocabPath);
        var vocab = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, int>>(jsonText);
        tokens = new string[vocab.Count];

        foreach (var item in vocab)
        {
            tokens[item.Value] = item.Key;
        }

        Debug.Log("Debug_log: ✅ vocab.json successfully loaded!");
    }




    void EncodeAudio()
    {
        Debug.Log("Debug_log: ✅ Beggining of EncodeAudio() reached...");


        using var input = new TensorFloat(new TensorShape(1, numSamples), data);

        // Pad out to 30 seconds at 16khz if necessary
        using var input30seconds = ops.Pad(input, new int[] { 0, 0, 0, maxSamples - numSamples });
        Debug.Log($"Debug_log: 🔍 spectroEngine is {(spectroEngine == null ? "NULL" : "ASSIGNED")}");
        Debug.Log($"Debug_log: 🔍 input30seconds is {(input30seconds == null ? "NULL" : "ASSIGNED")}");


        try
        {
            Debug.Log("Debug_log: 🚀 Executing spectroEngine...");
            spectroEngine.Execute(input30seconds);
            Debug.Log("Debug_log: ✅ Execution completed!");

            var spectroOutput = spectroEngine.PeekOutput() as TensorFloat;
            Debug.Log("Debug_log: ✅ PeekOutput completed!");
            encoderEngine.Execute(spectroOutput);
            Debug.Log("Debug_log: ✅ encode engine exectuted!");


        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Debug_log: ❌ Exception in EncodeAudio(): {ex.Message}");
        }



        Debug.Log("Debug_log:  EncodeAudio() 3");

        encodedAudio = encoderEngine.PeekOutput() as TensorFloat;

        Debug.Log("Debug_log: 🔄 Encoding audio for Whisper processing...");
        if (audioClip == null)
        {
            Debug.LogError("Debug_log: ❌ ERROR: AudioClip is NULL in EncodeAudio()!");
            return;
        }

        // Simulated encoding step (actual encoding logic depends on implementation)
        Debug.Log("Debug_log: ✅ End of EncodeAudio() reached...");

       
    }
    void Update()
    {
        if (transcribe && currentToken < outputTokens.Length - 1)
        {
            using var tokensSoFar = new TensorInt(new TensorShape(1, outputTokens.Length), outputTokens);

            var inputs = new Dictionary<string, Tensor>
        {
            {"encoded_audio", encodedAudio },
            {"tokens", tokensSoFar }
        };

            decoderEngine.Execute(inputs);
            var tokensOut = decoderEngine.PeekOutput() as TensorFloat;

            using var tokensPredictions = ops.ArgMax(tokensOut, 2, false);
            tokensPredictions.MakeReadable();

            int ID = tokensPredictions[currentToken];

            Debug.Log($"Debug_log: 🧐 Token received: ID {ID}");

            // Prevent duplicate processing of the same token
            if (currentToken > 0 && outputTokens[currentToken] == ID)
            {
                Debug.Log($"Debug_log: ⚠️ Skipping duplicate token ID {ID}");
                return;
            }

            outputTokens[++currentToken] = ID;

            if (ID == END_OF_TEXT)
            {
                Debug.Log("Debug_log: ✅ END_OF_TEXT token received. Stopping transcription.");
                transcribe = false;
                isTranscribing = false;
                speechRecognitionController.onResponse.Invoke(outputString); // Ensure this triggers UI update
                return;
            }
            else if (ID >= tokens.Length)
            {
                Debug.Log($"Debug_log: ❓ Unknown token ID {ID}. Ignoring.");
                speechRecognitionController.onResponse.Invoke(outputString);
            }
            else
            {
                string newText = GetUnicodeText(tokens[ID]);

                // Prevent exact duplicate words from being appended
                if (!outputString.EndsWith(newText))
                {
                    Debug.Log($"Debug_log: ✏️ Appending token '{newText}' to transcript.");
                    outputString += newText;
                }
                else
                {
                    Debug.Log($"Debug_log: ⚠️ Skipping duplicate word '{newText}'");
                }

                Debug.Log($"Debug_log: 📝 Current transcript: {outputString}");
            }
        }
    }

    // Translates encoded special characters to Unicode
    string GetUnicodeText(string text)
    {
        var bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(ShiftCharacterDown(text));
        return Encoding.UTF8.GetString(bytes);
    }

    string ShiftCharacterDown(string text)
    {
        string outText = "";
        foreach (char letter in text)
        {
            outText += ((int)letter <= 256) ? letter :
                (char)whiteSpaceCharacters[(int)(letter - 256)];
        }
        return outText;
    }

    void SetupWhiteSpaceShifts()
    {
        for (int i = 0, n = 0; i < 256; i++)
        {
            if (IsWhiteSpace((char)i)) whiteSpaceCharacters[n++] = i;
        }
    }

    bool IsWhiteSpace(char c)
    {
        return !(('!' <= c && c <= '~') || ('�' <= c && c <= '�') || ('�' <= c && c <= '�'));
    }

    private void OnDestroy()
    {
        decoderEngine?.Dispose();
        encoderEngine?.Dispose();
        spectroEngine?.Dispose();
        ops?.Dispose();
        allocator?.Dispose();
    }
}
