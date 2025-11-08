using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Events;
using System.IO;
using UnityEngine.Android;


public class SpeechRecognitionController : MonoBehaviour {
    [SerializeField] private UnityEvent onStartRecording;
    [SerializeField] private UnityEvent onSendRecording;
    [SerializeField] public UnityEvent<string> onResponse;
    [SerializeField] private TMP_Dropdown m_deviceDropdown;
    [SerializeField] private Image m_progress;

    public RunWhisper runWhisper; // This is the reference to the RunWhisper script

    private string m_deviceName;
    private AudioClip m_clip;
    private byte[] m_bytes;
    private bool m_recording;

    private void Awake()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("No microphones detected!");
            return;
        }

        // Select the first available microphone
        m_deviceName = Microphone.devices[0];
        Debug.Log("Microphone detected: " + m_deviceName);

        foreach (var device in Microphone.devices)
        {
            Debug.Log("Available microphone: " + device);
            m_deviceDropdown.options.Add(new TMP_Dropdown.OptionData(device));
        }

        m_deviceDropdown.value = 0;
        m_deviceDropdown.onValueChanged.AddListener(OnDeviceChanged);
    }

    

    void Start()
    {
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Permission.RequestUserPermission(Permission.Microphone);
        }
    }


/// <summary>
/// This method is called when the user selects a different device from the dropdown
/// </summary>
/// <param name="index"></param>
private void OnDeviceChanged(int index) {
        m_deviceName = Microphone.devices[index];
    }

    /// <summary>
    /// This method is called when the user clicks the button
    /// </summary>
    public void Click()
    {
        Debug.Log("SpeechRecognition: Click detected!");
        if (!m_recording)
        {
            StartRecording();
        }
        else
        {
            StopRecording();
        }
    }


    /// <summary>
    /// Start recording the user's voice
    /// </summary>
    private void StartRecording()
    {
        Debug.Log("🎤 StartRecording() triggered!"); // Log that recording is starting

        m_clip = Microphone.Start(m_deviceName, false, 10, 16000);
        m_recording = true;
        onStartRecording.Invoke();

        Debug.Log($"🎤 Recording started on device: {m_deviceName}");
    }


    /// <summary>
    /// Stop recording the user's voice and send the audio to the Whisper Model
    /// </summary>
    private void StopRecording() {

        if (!Microphone.IsRecording(m_deviceName))
        {
            Debug.LogWarning("Debug_log: ❌ No active microphone recording to stop.");
            return;
        }

        var position = Microphone.GetPosition(m_deviceName);
        Microphone.End(m_deviceName);
        m_recording = false;
        if (m_clip == null)
        {
            Debug.LogError("❌ No audio was recorded!");
            return;
        }
        if (position == 0)
        {
            Debug.LogWarning("Debug_log: ❌ No audio recorded. Position is 0.");
            return;
        }

        // Extract the actual recorded data (instead of full 10s buffer)
        float[] samples = new float[position * m_clip.channels];
        m_clip.GetData(samples, 0);

        AudioClip trimmedClip = AudioClip.Create("TrimmedClip", position, m_clip.channels, m_clip.frequency, false);
        trimmedClip.SetData(samples, 0);

        m_clip = trimmedClip;  // Replace with trimmed version

        Debug.Log($"Debug_log: 🎤 Trimmed Audio Length: {m_clip.length} sec, Samples: {position}");

        SendRecording();
    }

    /// <summary>
    /// Run the Whisper Model with the audio clip to transcribe the user's voice
    /// </summary>
    /*private void SendRecording() {
        Debug.Log("📤 Sending recording to Whisper...");
        onSendRecording.Invoke();
        runWhisper.audioClip = m_clip;
        if (runWhisper == null)
        {
            Debug.LogError("❌ RunWhisper is NULL! Cannot transcribe.");
            return;
        }
        Debug.Log("✅ runWhisper is assigned. Proceeding with transcription.");
        runWhisper.Transcribe();
        Debug.Log("📤 Transcription started!");

    }*/
    private void SendRecording()
    {
        Debug.Log("Debug_log: 📤 Sending recording to Whisper...");

        if (runWhisper == null)
        {
            Debug.LogError("Debug_log: ❌ RunWhisper is NULL! Cannot transcribe.");
            return;
        }

        Debug.Log($"Debug_log: ✅ runWhisper is assigned. Proceeding with transcription. Clip length: {m_clip.length} seconds, Samples: {m_clip.samples}, Channels: {m_clip.channels}");

        if (m_clip == null)
        {
            Debug.LogError("Debug_log: ❌ ERROR: AudioClip is NULL in SendRecording!");
            return;
        }

        Debug.Log("Debug_log: 📤 AudioClip assigned to RunWhisper. Calling Transcribe()...");
        onSendRecording.Invoke();
        runWhisper.audioClip = m_clip;
        runWhisper.Transcribe();
    }



    private void Update() {
        if (!m_recording) {
            return;
        }

        m_progress.fillAmount = (float)Microphone.GetPosition(m_deviceName) / m_clip.samples;
        int micPosition = Microphone.GetPosition(m_deviceName);
        //Debug.Log($"🎤 Microphone position: {micPosition}");

        if (Microphone.GetPosition(m_deviceName) >= m_clip.samples) {
            StopRecording();
        }
    }
}
