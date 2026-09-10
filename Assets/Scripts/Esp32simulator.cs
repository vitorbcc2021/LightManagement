using System;
using UnityEngine;
using M2MqttUnity;
using uPLibrary.Networking.M2Mqtt.Messages;

public class ESP32Simulator : M2MqttUnityClient
{
    [Header("Light Management")]
    public string topicBase = "lightmanagement/device1";

    [Header("Luzes LED")]
    public Light[] ledLights;
    public float minLightIntensity = 10000f;
    public float maxLightIntensity = 50000f;

    [Header("Temperatura")]
    public float ambientTemperature = 25f;
    public float heatingRate = 0.8f;
    public float coolingRate = 1.2f;
    public float maxTemperature = 75f;
    public float sensorNoise = 0.3f;
    public float telemetryInterval = 2f;

    private bool _isOn = false;
    private int _brightness = 255;
    private float _temp;
    private float _telTimer = 0f;

    private string TopicCmd => topicBase + "/cmd";
    private string TopicStatus => topicBase + "/status";
    private string TopicTelemetry => topicBase + "/telemetry";

    private long _clockOffset = 0;
    private bool _isClockSynced = false;

    protected override void Start()
    {
        _temp = ambientTemperature;
        ApplyBrightness();
        base.Start();
    }

    protected override void Update()
    {
        base.Update();
        SimulateTemperature();

        _telTimer += Time.deltaTime;
        if (_telTimer >= telemetryInterval)
        {
            _telTimer = 0f;
            PublishTelemetry();
        }
    }

    protected override void SubscribeTopics()
    {
        client.Subscribe(
            new string[] { TopicCmd },
            new byte[] { MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE }
        );
        Debug.Log("[ESP32Sim] Inscrito em: " + TopicCmd);
    }

    protected override void OnConnected()
    {
        base.OnConnected();
        PublishStatus();
    }

    protected override void DecodeMessage(string topic, byte[] message)
    {
        if (topic != TopicCmd) return;

        string json = System.Text.Encoding.UTF8.GetString(message);

        if (json.Contains("\"power\""))
            SetPower(json.Contains("\"on\""));
        else if (json.Contains("\"brightness\""))
            SetBrightness(ParseInt(json, "value"));

        long sentAt = ParseLong(json, "sentAt");
        if (sentAt <= 0) return;

        long currentMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (!_isClockSynced)
        {
            _clockOffset = currentMs - sentAt;
            _isClockSynced = true;
            Debug.Log($"<color=yellow>[SISTEMA]</color> Relógios calibrados com sucesso! Desvio base: {_clockOffset} ms");
            return;
        }

        long realLatencyMs = (currentMs - _clockOffset) - sentAt;

        Debug.Log($"<color=green>[LATÊNCIA APP -> UNITY]</color> Tempo de resposta visual: {realLatencyMs} ms");
    }

    long ParseLong(string json, string key)
    {
        string search = "\"" + key + "\":";
        int idx = json.IndexOf(search);
        if (idx < 0) return 0;
        int start = idx + search.Length;
        int end = start;
        while (end < json.Length && char.IsDigit(json[end])) end++;
        return long.TryParse(json.Substring(start, end - start), out long r) ? r : 0;
    }

    void SetPower(bool on)
    {
        _isOn = on;
        ApplyBrightness();
        PublishStatus();
    }

    void SetBrightness(int value)
    {
        _brightness = Mathf.Clamp(value, 0, 255);
        if (_isOn) ApplyBrightness();
    }

    void ApplyBrightness()
    {
        if (ledLights == null) return;
        foreach (var light in ledLights)
        {
            if (light == null) continue;
            if (!_isOn || _brightness == 0) { light.enabled = false; continue; }
            light.intensity = Mathf.Lerp(minLightIntensity, maxLightIntensity, _brightness / 255f);
            light.range = 150f;
            light.enabled = true;
        }
    }

    void PublishStatus()
    {
        if (client == null || !client.IsConnected) return;
        float temp = (float)Math.Round(_temp + UnityEngine.Random.Range(-sensorNoise, sensorNoise), 1);
        client.Publish(TopicStatus,
            System.Text.Encoding.UTF8.GetBytes(
                "{\"power\":\"" + (_isOn ? "on" : "off") +
                "\",\"brightness\":" + _brightness +
                ",\"temperature\":" + temp.ToString("F1") + "}"),
            MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, false);
    }

    void PublishTelemetry()
    {
        if (client == null || !client.IsConnected) return;
        float temp = (float)Math.Round(_temp + UnityEngine.Random.Range(-sensorNoise, sensorNoise), 1);
        client.Publish(TopicTelemetry,
            System.Text.Encoding.UTF8.GetBytes(
                "{\"temperature\":" + temp.ToString("F1") +
                ",\"brightness\":" + _brightness + "}"),
            MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, false);
    }

    void SimulateTemperature()
    {
        if (_isOn)
        {
            _temp += heatingRate * (_brightness / 255f) * Time.deltaTime;
            _temp = Mathf.Min(_temp, maxTemperature);
        }
        else if (_temp > ambientTemperature)
        {
            _temp -= coolingRate * Time.deltaTime;
            _temp = Mathf.Max(_temp, ambientTemperature);
        }
    }

    void OnGUI()
    {
        bool connected = client != null && client.IsConnected;
        string text =
            "[ESP32 Simulator]\n" +
            "MQTT  : " + (connected ? "Conectado" : "Offline") + "\n" +
            "Power : " + (_isOn ? "LIGADO" : "DESLIGADO") + "\n" +
            "Brilho: " + Mathf.RoundToInt(_brightness / 255f * 100f) + "%\n" +
            "Temp  : " + _temp.ToString("F1") + " C";

        GUI.color = Color.black;
        GUI.Label(new Rect(11, 11, 260, 110), text);
        GUI.color = _isOn ? new Color(1f, 0.7f, 0.1f) : Color.gray;
        GUI.Label(new Rect(10, 10, 260, 110), text);
    }

    int ParseInt(string json, string key)
    {
        string search = "\"" + key + "\":";
        int idx = json.IndexOf(search);
        if (idx < 0) return 0;
        int start = idx + search.Length;
        int end = start;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-')) end++;
        return int.TryParse(json.Substring(start, end - start), out int r) ? r : 0;
    }
}