using UnityEngine;
using UnityEngine.Serialization;

public class PatientMonitor : MonoBehaviour
{
    public enum PatientDataType
    {
        ECG_GRAPH,
        HR_MAX,
        HR_MIN,
        PLETH_GRAPH,
        SPO2_MAX,
        SPO2_MIN,
        RR_GRAPH,
        RR_MAX,
        RR_MIN,
        BP_SP,
        BP_DP
    }

    public enum ConnectType
    {
        ECG,
        SpO2,
        BPCuff,
        END
    }

    public enum PatientMonitorGraphType
    {
        ECG_II,
        Pleth,
        RR
    }

    [SerializeField] private GameObject screen;
    [SerializeField] private MonitorGraph[] graphGroups = new MonitorGraph[3];
    [SerializeField] private UnityEngine.UI.Text bpText;
    private bool bpCuffConnected = false;
    [SerializeField] private float bpSp = 0f;
    [SerializeField] private float bpDp = 0f;

    [Header("Debug Connect Toggle")]
    [SerializeField] private bool debugEcgConnect = false;
    [SerializeField] private bool debugSpo2Connect = false;
    [SerializeField] private bool debugBpCuffConnect = false;

    private bool debugToggleInitialized = false;
    private bool lastDebugEcgConnect = false;
    private bool lastDebugSpo2Connect = false;
    private bool lastDebugBpCuffConnect = false;

    private const string BP_UNKNOWN_TEXT = "?/? (?)";

    private void Update()
    {
        ApplyDebugConnectToggleChanges();
    }

    public void SetConnect(ConnectType _type, bool _connect)
    {
        switch (_type)
        {
            case ConnectType.ECG:
                SetMonitorConnect(PatientMonitorGraphType.ECG_II, _connect);
                SetMonitorConnect(PatientMonitorGraphType.RR, _connect);
                debugEcgConnect = _connect;
                lastDebugEcgConnect = _connect;
                break;

            case ConnectType.SpO2:
                SetMonitorConnect(PatientMonitorGraphType.Pleth, _connect);
                debugSpo2Connect = _connect;
                lastDebugSpo2Connect = _connect;
                break;

            case ConnectType.BPCuff:
                bpCuffConnected = _connect;
                debugBpCuffConnect = _connect;
                lastDebugBpCuffConnect = _connect; ;
                if (_connect)
                    UpdateBP();
                break;
        }

        if (_connect && screen != null)
        {
            screen.SetActive(true);
        }
    }

    public void SetPatientData(PatientDataType _name, string _value)
    {
        switch (_name)
        {
            case PatientDataType.ECG_GRAPH:
                SetGraphData(PatientMonitorGraphType.ECG_II, _value);
                break;
            case PatientDataType.HR_MAX:
                SetGroupRateMax(PatientMonitorGraphType.ECG_II, _value);
                break;
            case PatientDataType.HR_MIN:
                SetGroupRateMin(PatientMonitorGraphType.ECG_II, _value);
                break;

            case PatientDataType.PLETH_GRAPH:
                SetGraphData(PatientMonitorGraphType.Pleth, _value);
                break;
            case PatientDataType.SPO2_MAX:
                SetGroupRateMax(PatientMonitorGraphType.Pleth, _value);
                break;
            case PatientDataType.SPO2_MIN:
                SetGroupRateMin(PatientMonitorGraphType.Pleth, _value);
                break;

            case PatientDataType.RR_GRAPH:
                SetGraphData(PatientMonitorGraphType.RR, _value);
                break;
            case PatientDataType.RR_MAX:
                SetGroupRateMax(PatientMonitorGraphType.RR, _value);
                break;
            case PatientDataType.RR_MIN:
                SetGroupRateMin(PatientMonitorGraphType.RR, _value);
                break;

            case PatientDataType.BP_SP:
                if (TryParseFloat(_value, out float _sp))
                {
                    bpSp = _sp;
                }
                break;
            case PatientDataType.BP_DP:
                if (TryParseFloat(_value, out float _dp))
                {
                    bpDp = _dp;
                }
                break;
        }
    }

    public MonitorGraph GetGroup(PatientMonitorGraphType _type)
    {
        int _idx = (int)_type;
        if (graphGroups != null && graphGroups.Length > _idx)
        {
            return graphGroups[_idx];
        }

        return null;
    }

    public void UpdateBP()
    {
        if (bpText == null)
        {
            return;
        }

        if (bpCuffConnected)
        {
            int _sbp = Mathf.RoundToInt(bpSp);
            int _dbp = Mathf.RoundToInt(bpDp);
            int _map = Mathf.RoundToInt((bpSp + 2f * bpDp) / 3f);
            bpText.text = $"{_sbp}/{_dbp} ({_map})";
        }
        else
        {
            bpText.text = BP_UNKNOWN_TEXT;
        }
    }

    public void AddShockData()
    {
        MonitorGraph _ecg = GetGroup(PatientMonitorGraphType.ECG_II);
        if (_ecg != null)
        {
            _ecg.AddWaveOnetime(GetGraphData("ecg_shock"), 25f);
        }

        MonitorGraph _pleth = GetGroup(PatientMonitorGraphType.Pleth);
        if (_pleth != null)
        {
            _pleth.AddWaveOnetime(GetGraphData("pleth_shock"), 25f);
        }
    }

    private GraphData GetGraphData(string _graphName)
    {
        return Resources.Load<GraphData>(_graphName);
    }

    private static bool TryParseFloat(string _value, out float _result)
    {
        return float.TryParse(_value, out _result);
    }

    private void SetMonitorConnect(PatientMonitorGraphType _type, bool _connect)
    {
        MonitorGraph _group = GetGroup(_type);
        if (_group != null)
        {
            _group.Connect = _connect;
        }
    }

    private void SetGraphData(PatientMonitorGraphType _type, string _graphName)
    {
        MonitorGraph _group = GetGroup(_type);
        if (_group != null)
        {
            _group.SetGraphData(GetGraphData(_graphName));
        }
    }

    private void SetGroupRateMin(PatientMonitorGraphType _type, string _value)
    {
        if (!TryParseFloat(_value, out float _rate))
        {
            return;
        }

        MonitorGraph _group = GetGroup(_type);
        if (_group != null)
        {
            _group.SetRateMin(_rate);
        }
    }

    private void SetGroupRateMax(PatientMonitorGraphType _type, string _value)
    {
        if (!TryParseFloat(_value, out float _rate))
        {
            return;
        }

        MonitorGraph _group = GetGroup(_type);
        if (_group != null)
        {
            _group.SetRateMax(_rate);
        }
    }

    private void ApplyDebugConnectToggleChanges()
    {
        if (!debugToggleInitialized)
        {
            SetConnect(ConnectType.ECG, debugEcgConnect);
            SetConnect(ConnectType.SpO2, debugSpo2Connect);
            SetConnect(ConnectType.BPCuff, debugBpCuffConnect);
            debugToggleInitialized = true;
            return;
        }

        if (debugEcgConnect != lastDebugEcgConnect)
        {
            SetConnect(ConnectType.ECG, debugEcgConnect);
        }

        if (debugSpo2Connect != lastDebugSpo2Connect)
        {
            SetConnect(ConnectType.SpO2, debugSpo2Connect);
        }

        if (debugBpCuffConnect != lastDebugBpCuffConnect)
        {
            SetConnect(ConnectType.BPCuff, debugBpCuffConnect);
        }
    }
}
