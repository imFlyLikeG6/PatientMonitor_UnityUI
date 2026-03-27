using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class MonitorGraph : MonoBehaviour
{
    [SerializeField] private PatientMonitor.PatientMonitorGraphType graphType;
    public PatientMonitor.PatientMonitorGraphType GraphType => graphType;

    [SerializeField] private bool connect = false;
    public bool Connect
    {
        get => connect;
        set
        {
            connect = value;
            if (connect && connectGraph != null)
            {
                SyncConnectGraph();
            }
        }
    }

    [Header("Renderer")]
    [SerializeField] private Image[] chartMask;      // 2개 마스크(Fill)로 스캔 효과
    [SerializeField] private LineGraph[] lineGraph;  // 2개 라인 그래프

    [Header("Wave Source")]
    [SerializeField] private GraphData graphData;
    [SerializeField] private float loopTime = 6f;

    [Header("Rate")]
    [SerializeField] private float rateDataMin = 80f;
    [SerializeField] private float rateDataMax = 90f;
    [SerializeField] private float currentRate;
    [SerializeField] private Text rateText;

    [Header("Link")]
    [SerializeField] private MonitorGraph connectGraph;

    [Header("Follower")]
    [SerializeField] private RectTransform positionFollower;
    [SerializeField] private RectTransform followerTrackArea;

    [Header("Debug")]
    [SerializeField, Range(0f, 2f)] private float currentPosition;
    [SerializeField] private float lastPosition = 0f;
    [SerializeField] private float currentOffset;
    [SerializeField] private int currentGraphIdx;

    // 더블 버퍼(그래프 2장)를 번갈아 쓰기 위한 라인 포인트 저장소
    private readonly List<Vector2>[] lineEntries = new List<Vector2>[2] { new List<Vector2>(256), new List<Vector2>(256) };
    private readonly List<GraphData> tempGraph = new List<GraphData>();
    private readonly List<float> tempRate = new List<float>();

    private const float GRAPH_MAX = 100f;
    private const float NONE_VALUE = 25f;
    private const float NONE_POSITION_UNIT = 10f;
    // rate가 0으로 내려가도 분모가 0이 되지 않게 보정
    private const float MIN_RATE = 1f;
    private const int MAX_WAVE_CATCHUP_PER_FRAME = 32;

    private Coroutine updateRoutine;

    // 활성화 시 모니터 업데이트 루프 시작
    private void OnEnable()
    {
        updateRoutine = StartCoroutine(UpdatePatientMonitor());
    }

    // 비활성화 시 루프 중지 및 현재 진행 위치 저장
    private void OnDisable()
    {
        lastPosition = currentPosition;
        if (updateRoutine != null)
        {
            StopCoroutine(updateRoutine);
            updateRoutine = null;
        }
    }

    // 인스펙터 값 안전 범위 보정
    private void OnValidate()
    {
        loopTime = Mathf.Max(0.1f, loopTime);
        rateDataMin = Mathf.Max(1f, rateDataMin);
        rateDataMax = Mathf.Max(rateDataMin, rateDataMax);
        currentGraphIdx = Mathf.Clamp(currentGraphIdx, 0, 1);
    }

    // 파형 데이터 소스를 교체하고, 연결 그래프가 있으면 위치 동기화
    public void SetGraphData(GraphData newGraphData)
    {
        graphData = newGraphData;
        if (connectGraph != null)
        {
            SyncConnectGraph();
        }
    }

    // rate 최소값 설정
    public void SetRateMin(float min)
    {
        rateDataMin = min;
        if (connectGraph != null)
        {
            SyncConnectGraph();
        }
    }

    // rate 최대값 설정
    public void SetRateMax(float max)
    {
        rateDataMax = max;
        if (connectGraph != null)
        {
            SyncConnectGraph();
        }
    }

    // 현재 rate 값 반환
    public float GetCurrentRate()
    {
        return currentRate;
    }

    // 1회성 파형(예: shock)을 큐에 추가
    public void AddWaveOnetime(GraphData _oneShotData, float _rate)
    {
        if (_oneShotData == null)
        {
            return;
        }

        tempGraph.Add(_oneShotData);
        tempRate.Add(_rate);

        if (connectGraph != null)
        {
            SyncConnectGraph();
        }
    }

    // 현재 활성 그래프 버퍼의 라인 포인트 목록 반환
    private List<Vector2> GetCurrentEntry()
    {
        return lineEntries[currentGraphIdx];
    }

    // 현재 활성 그래프 버퍼의 마스크 반환
    private Image GetCurrentMask()
    {
        return chartMask[currentGraphIdx];
    }

    // 현재 활성 그래프 버퍼의 LineGraph 반환
    private LineGraph GetCurrentLineGraph()
    {
        return lineGraph[currentGraphIdx];
    }

    // 현재 버퍼의 포인트를 LineGraph에 반영
    private void SetDirty()
    {
        LineGraph _lineGraph = GetCurrentLineGraph();
        if (_lineGraph == null)
        {
            return;
        }

        List<Vector2> _entries = GetCurrentEntry();
        _lineGraph.SetData(_entries);
    }

    // 더블 버퍼 인덱스를 0/1로 전환
    private void ChangeCurrentGraph()
    {
        currentGraphIdx = currentGraphIdx == 1 ? 0 : 1;
    }

    // 연결된 그래프의 진행 상태를 그대로 동기화
    private void SyncConnectGraph()
    {
        currentPosition = connectGraph.currentPosition;
        currentOffset = connectGraph.currentOffset;
    }

    private IEnumerator UpdatePatientMonitor()
    {
        Image _currentMask = GetCurrentMask();
        float _lastUpdate = currentPosition;
        float _startTime = Time.time;

        while (isActiveAndEnabled)
        {
            if (connectGraph != null)
            {
                // 연결된 그래프가 있으면 위치를 그대로 따라간다.
                currentPosition = connectGraph.currentPosition;
            }
            else
            {
                // [0..1] 범위 스캔 진행률: (경과시간 / 한 루프 시간) + 이전 정지 위치
                currentPosition = (Time.time - _startTime) / loopTime + lastPosition;
            }

            if (_currentMask != null)
            {
                _currentMask.fillAmount = currentPosition;
            }
            UpdateFollowerPosition(_currentMask);

            if (_lastUpdate < currentPosition)
            {
                // 프레임 드랍 등으로 currentPosition이 크게 점프하면
                // 파형도 같은 프레임에서 여러 번 추가해 빈 구간을 방지한다.
                int _catchupCount = 0;
                while (_lastUpdate < currentPosition && _catchupCount < MAX_WAVE_CATCHUP_PER_FRAME)
                {
                    _lastUpdate = AddWave();
                    _catchupCount++;

                    if (currentOffset > GRAPH_MAX)
                    {
                        _lastUpdate = 101f;
                        break;
                    }
                }

                UpdateRateText();
            }

            if (currentPosition >= 1f)
            {
                if (_currentMask != null)
                {
                    _currentMask.fillAmount = 1f;
                }

                ChangeCurrentGraph();
                _currentMask = GetCurrentMask();
                if (_currentMask != null)
                {
                    _currentMask.fillAmount = 0f;
                    _currentMask.transform.SetAsLastSibling();
                }
                UpdateFollowerPosition(_currentMask);

                GetCurrentEntry().Clear();
                AddWave();              // 이전 그래프와 이어지도록 앞부분 채움
                _lastUpdate = AddWave(); // 실제 다음 파형 추가
                UpdateRateText();
                if (currentOffset > GRAPH_MAX)
                {
                    _lastUpdate = 101f;
                }

                _startTime = Time.time;
                lastPosition = 0f;
            }

            yield return null;
        }
    }

    // currentPosition(0~1)을 따라 지정된 오브젝트를 x축으로 이동
    private void UpdateFollowerPosition(Image _currentMask)
    {
        if (positionFollower == null)
        {
            return;
        }

        RectTransform _trackArea = followerTrackArea;
        if (_trackArea == null && _currentMask != null)
        {
            _trackArea = _currentMask.rectTransform;
        }

        if (_trackArea == null)
        {
            return;
        }

        float _normalized = Mathf.Repeat(currentPosition, 1f);
        float _x = (_normalized - _trackArea.pivot.x) * _trackArea.rect.width;
        Vector2 _anchored = positionFollower.anchoredPosition;
        _anchored.x = _x;
        positionFollower.anchoredPosition = _anchored;
    }

    // 현재 연결 상태/소스에 따라 UI의 rate 텍스트 갱신
    private void UpdateRateText()
    {
        if (rateText == null)
        {
            return;
        }

        if (!Connect)
        {
            rateText.text = string.Empty;
            return;
        }

        int _rate;
        if (connectGraph != null)
        {
            _rate = Mathf.FloorToInt(Random.Range(rateDataMin, rateDataMax));
        }
        else
        {
            _rate = Mathf.FloorToInt(currentRate);
        }

        rateText.text = _rate.ToString();
    }

    // 현재 시점에 필요한 파형 포인트를 생성/추가하고, 다음 업데이트 기준 위치 반환
    private float AddWave()
    {
        float _waveLength;
        int _waveCount = 1;

        GraphData _activeGraphData = graphData;
        if (tempGraph.Count > 0)
        {
            if (currentOffset < GRAPH_MAX || tempGraph.Count > tempRate.Count)
            {
                _activeGraphData = tempGraph[0];
            }
        }

        if (_activeGraphData != null)
        {
            _waveCount = _activeGraphData.wave_count;
        }

        if (currentOffset > GRAPH_MAX)
        {
            // 100을 넘은 상태는 다음 루프로 넘어간 꼬리 구간.
            // 동일 파형 길이를 유지하며 offset을 앞으로 당겨 연속성 유지.
            float _safeRate = Mathf.Max(MIN_RATE, currentRate);
            // 파형 길이(그래프 x축 단위):
            // beat duration(sec)=60/rate, 해당 구간을 loopTime 기준 0~100 스케일로 변환
            // => waveLength = (60 / rate) * waveCount * (GRAPH_MAX / loopTime)
            _waveLength = 60f * _waveCount * GRAPH_MAX / (_safeRate * loopTime);
            currentOffset = (currentOffset - GRAPH_MAX) - _waveLength;
        }
        else
        {
            if (connectGraph != null)
            {
                currentRate = connectGraph.currentRate;
            }
            else
            {
                currentRate = Random.Range(rateDataMin, rateDataMax);
            }

            if (tempGraph.Count > 0 && tempGraph.Count == tempRate.Count)
            {
                currentRate = tempRate[0];
                tempRate.RemoveAt(0);
            }

            float _safeRate = Mathf.Max(MIN_RATE, currentRate);
            // 현재 rate로 이번 주기의 x축 길이를 계산
            _waveLength = 60f * _waveCount * GRAPH_MAX / (_safeRate * loopTime);
        }

        float _lastWavePosition = currentOffset;
        List<Vector2> _entries = GetCurrentEntry();

        if (_activeGraphData != null && currentRate > 0f && Connect)
        {
            float _frequency;
            if (_activeGraphData.interval_time == 0f)
            {
                // interval_time이 없으면 rate 기반으로 샘플 데이터의 x축 배율을 산출:
                // 1박동 시간(60/rate)이 loopTime에서 차지하는 비율에 wave_count를 반영
                _frequency = _activeGraphData.wave_count / (currentRate / 60f * loopTime);
            }
            else
            {
                // interval_time이 있으면 고정 주기 기반 배율 사용
                _frequency = _activeGraphData.interval_time / loopTime;
            }

            for (int _i = 0; _i < _activeGraphData.data.Count; _i++)
            {
                GraphData.Data _d = _activeGraphData.data[_i];
                float _position = (_d.position + Random.Range(-_d.random_position, _d.random_position)) * _frequency + currentOffset;
                float _value = _d.value + Random.Range(-_d.random_value, _d.random_value);

                _lastWavePosition = _position;
                AddEntryIfInRange(_entries, _position, _value);
            }
        }
        else
        {
            AddEntryIfInRange(_entries, currentOffset, NONE_VALUE);
            AddEntryIfInRange(_entries, currentOffset + _waveLength, NONE_VALUE);
        }

        float _returnOffset = currentOffset;
        currentOffset += _waveLength;

        // 마지막 실제 파형점 이후가 너무 길게 비면 보간이 어색해져서,
        // NONE_POSITION_UNIT 간격으로 기준점(NONE_VALUE)을 채워 선을 안정화
        int _noneCount = Mathf.FloorToInt((currentOffset - _lastWavePosition) / NONE_POSITION_UNIT);
        for (int _i = 1; _i <= _noneCount; _i++)
        {
            AddEntryIfInRange(_entries, _lastWavePosition + (_i * NONE_POSITION_UNIT), NONE_VALUE);
        }

        SetDirty();

        if (tempGraph.Count > 0 && tempRate.Count < tempGraph.Count && currentOffset < GRAPH_MAX)
        {
            tempGraph.RemoveAt(0);
        }

        // 현재 업데이트 지점을 0~1 fillAmount 기준으로 반환
        return _returnOffset * 0.01f;
    }

    // 그래프 x 범위(0~100) 안의 점만 추가
    private static void AddEntryIfInRange(List<Vector2> _entries, float _position, float _value)
    {
        if (_position < 0f || _position > GRAPH_MAX)
        {
            return;
        }

        _entries.Add(new Vector2(_position, Mathf.Clamp(_value, 0f, 100f)));
    }
}
