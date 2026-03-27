using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEngine.Serialization;

[ExecuteAlways]
[RequireComponent(typeof(CanvasRenderer))]
public class LineGraph : MaskableGraphic
{
    // 수치 안정성/조인 계산에서 쓰는 최소값 상수들
    private const float MIN_DISTANCE_SQR = 0.0001f;
    private const float MIN_MITER_DENOMINATOR = 0.15f;
    private const float MIN_AXIS_VALUE = 0.0001f;

    [Header("Line Style")]
    [FormerlySerializedAs("_minLineThickness")]
    [SerializeField] private float minLineThickness = 1f;
    [FormerlySerializedAs("_lineThickness")]
    [SerializeField] private float lineThickness = 3f;
    [FormerlySerializedAs("_axisMaxValue")]
    [SerializeField] private float axisMaxValue = 100f;
    [FormerlySerializedAs("_useCurvedLine")]
    [SerializeField] private bool useCurvedLine = true;
    [FormerlySerializedAs("_samplesPerSegment")]
    [SerializeField, Range(1, 30)] private int samplesPerSegment = 10;
    [FormerlySerializedAs("_miterLimit")]
    [SerializeField, Range(1f, 10f)] private float miterLimit = 3f;

    [Header("Data (x:0~100, y:0~100)")]
    [FormerlySerializedAs("_dataPoints")]
    [SerializeField] private Vector2[] dataPoints;

    // GC를 줄이기 위해 매 프레임 재사용하는 버퍼
    private readonly List<Vector2> renderPointsBuffer = new List<Vector2>(256);
    private readonly List<Vector2> dedupedPointsBuffer = new List<Vector2>(256);
    private float[] cumulativeLengthBuffer = new float[256];
    private Vector2[] inputPointsBuffer = new Vector2[0];
    private int dataPointCount = 0;

    public override Texture mainTexture => Texture2D.whiteTexture;

    protected override void OnEnable()
    {
        base.OnEnable();
        SetAllDirty();
    }

    protected override void Reset()
    {
        base.Reset();
        raycastTarget = false;
        color = Color.white;
        lineThickness = 3f;
        axisMaxValue = 100f;
        useCurvedLine = true;
        samplesPerSegment = 10;
        miterLimit = 3f;
        dataPoints = new[]
        {
            new Vector2(0f, 10f),
            new Vector2(20f, 40f),
            new Vector2(50f, 25f),
            new Vector2(80f, 70f),
            new Vector2(100f, 50f)
        };
        SetAllDirty();
    }

    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        SetVerticesDirty();
    }

    public void SetData(Vector2[] _points)
    {
        dataPoints = _points;
        dataPointCount = _points != null ? _points.Length : 0;
        Redraw();
    }

    public void SetData(List<Vector2> _points)
    {
        if (_points == null || _points.Count == 0)
        {
            dataPoints = null;
            dataPointCount = 0;
            Redraw();
            return;
        }

        EnsureInputBufferSize(_points.Count);
        for (int _i = 0; _i < _points.Count; _i++)
        {
            inputPointsBuffer[_i] = _points[_i];
        }

        dataPoints = inputPointsBuffer;
        dataPointCount = _points.Count;
        Redraw();
    }

    [ContextMenu("Redraw")]
    public void Redraw()
    {
        SetAllDirty();
    }

    private Vector2 ToLocalPosition(Vector2 _value, Vector2 _areaSize)
    {
        // 입력 데이터(0~axisMaxValue)를 RectTransform 로컬 좌표로 변환
        float _max = Mathf.Max(MIN_AXIS_VALUE, axisMaxValue);
        float _normalizedX = Mathf.Clamp01(_value.x / _max);
        float _normalizedY = Mathf.Clamp01(_value.y / _max);

        float _x = (_normalizedX - rectTransform.pivot.x) * _areaSize.x;
        float _y = (_normalizedY - rectTransform.pivot.y) * _areaSize.y;
        return new Vector2(_x, _y);
    }

    protected override void OnPopulateMesh(VertexHelper _vh)
    {
        _vh.Clear();

        if (dataPoints == null || dataPointCount < 2)
        {
            return;
        }

        Vector2 _areaSize = rectTransform.rect.size;
        float _thickness = Mathf.Max(minLineThickness, lineThickness);
        float _halfThickness = _thickness * 0.5f;
        // 1) 원본 포인트를 렌더용 포인트(곡선 샘플 포함)로 변환
        BuildRenderPoints(_areaSize, renderPointsBuffer);
        // 2) 너무 가까운 포인트를 제거해 조인/두께 계산 안정화
        RemoveNearDuplicatePoints(renderPointsBuffer, dedupedPointsBuffer);
        List<Vector2> _linePoints = dedupedPointsBuffer;

        if (_linePoints.Count < 2)
        {
            return;
        }

        EnsureCumulativeBufferSize(_linePoints.Count);
        cumulativeLengthBuffer[0] = 0f;
        float _totalLength = 0f;
        for (int _i = 1; _i < _linePoints.Count; _i++)
        {
            _totalLength += Vector2.Distance(_linePoints[_i - 1], _linePoints[_i]);
            cumulativeLengthBuffer[_i] = _totalLength;
        }
        _totalLength = Mathf.Max(MIN_AXIS_VALUE, _totalLength);

        UIVertex _v = UIVertex.simpleVert;
        _v.color = color;

        // 각 포인트에 대해 좌/우 버텍스를 추가해 리본(strip) 메쉬 생성
        for (int _i = 0; _i < _linePoints.Count; _i++)
        {
            Vector2 _p = _linePoints[_i];
            Vector2 _joinOffset = GetJoinOffset(_linePoints, _i, _halfThickness);
            float _u = cumulativeLengthBuffer[_i] / _totalLength;

            _v.position = _p - _joinOffset;
            _v.uv0 = new Vector2(_u, 0f);
            _vh.AddVert(_v);

            _v.position = _p + _joinOffset;
            _v.uv0 = new Vector2(_u, 1f);
            _vh.AddVert(_v);
        }

        // 인접한 두 포인트의 리본 버텍스를 삼각형 2개로 연결
        for (int _i = 0; _i < _linePoints.Count - 1; _i++)
        {
            int _baseIndex = _i * 2;
            int _nextIndex = _baseIndex + 2;
            _vh.AddTriangle(_baseIndex, _baseIndex + 1, _nextIndex + 1);
            _vh.AddTriangle(_nextIndex + 1, _nextIndex, _baseIndex);
        }
    }

    private Vector2 GetJoinOffset(List<Vector2> _points, int _index, float _halfThickness)
    {
        // 꺾이는 지점에서 miter join 오프셋을 계산해 이음새가 끊겨 보이는 문제를 완화
        int _last = _points.Count - 1;
        if (_index <= 0)
        {
            return GetSegmentNormal(_points[1] - _points[0]) * _halfThickness;
        }

        if (_index >= _last)
        {
            return GetSegmentNormal(_points[_last] - _points[_last - 1]) * _halfThickness;
        }

        Vector2 _dirPrev = (_points[_index] - _points[_index - 1]).normalized;
        Vector2 _dirNext = (_points[_index + 1] - _points[_index]).normalized;

        if (_dirPrev.sqrMagnitude < Mathf.Epsilon)
        {
            return GetSegmentNormal(_dirNext) * _halfThickness;
        }

        if (_dirNext.sqrMagnitude < Mathf.Epsilon)
        {
            return GetSegmentNormal(_dirPrev) * _halfThickness;
        }

        Vector2 _normalPrev = GetSegmentNormal(_dirPrev);
        Vector2 _normalNext = GetSegmentNormal(_dirNext);
        Vector2 _miter = _normalPrev + _normalNext;

        if (_miter.sqrMagnitude < 0.000001f)
        {
            return _normalNext * _halfThickness;
        }

        _miter.Normalize();
        float _denom = Mathf.Abs(Vector2.Dot(_miter, _normalNext));
        float _scale = _halfThickness / Mathf.Max(_denom, MIN_MITER_DENOMINATOR);
        _scale = Mathf.Min(_scale, _halfThickness * Mathf.Max(1f, miterLimit));
        return _miter * _scale;
    }

    private static Vector2 GetSegmentNormal(Vector2 _direction)
    {
        if (_direction.sqrMagnitude < Mathf.Epsilon)
        {
            return Vector2.up;
        }

        Vector2 _dir = _direction.normalized;
        return new Vector2(-_dir.y, _dir.x);
    }

    private static void RemoveNearDuplicatePoints(List<Vector2> _source, List<Vector2> _target)
    {
        _target.Clear();
        if (_source.Count == 0)
        {
            return;
        }

        _target.Add(_source[0]);
        for (int _i = 1; _i < _source.Count; _i++)
        {
            if ((_source[_i] - _target[_target.Count - 1]).sqrMagnitude > MIN_DISTANCE_SQR)
            {
                _target.Add(_source[_i]);
            }
        }
    }

    private void BuildRenderPoints(Vector2 _areaSize, List<Vector2> _output)
    {
        _output.Clear();
        if (dataPoints == null || dataPointCount == 0)
        {
            return;
        }

        if (!useCurvedLine || dataPointCount < 3)
        {
            for (int _i = 0; _i < dataPointCount; _i++)
            {
                _output.Add(ToLocalPosition(dataPoints[_i], _areaSize));
            }
            return;
        }

        // Catmull-Rom 보간으로 점 사이 중간 샘플을 추가해 자연스러운 곡선 생성
        _output.Add(ToLocalPosition(dataPoints[0], _areaSize));
        for (int _i = 0; _i < dataPointCount - 1; _i++)
        {
            int _aIndex = Mathf.Max(_i - 1, 0);
            int _bIndex = _i;
            int _cIndex = _i + 1;
            int _dIndex = Mathf.Min(_i + 2, dataPointCount - 1);

            Vector2 _a = ToLocalPosition(dataPoints[_aIndex], _areaSize);
            Vector2 _b = ToLocalPosition(dataPoints[_bIndex], _areaSize);
            Vector2 _c = ToLocalPosition(dataPoints[_cIndex], _areaSize);
            Vector2 _d = ToLocalPosition(dataPoints[_dIndex], _areaSize);

            for (int _s = 1; _s <= samplesPerSegment; _s++)
            {
                float _t = _s / (float)samplesPerSegment;
                _output.Add(GetCatmullRomPoint(_a, _b, _c, _d, _t));
            }
        }
    }

    private static Vector2 GetCatmullRomPoint(Vector2 _a, Vector2 _b, Vector2 _c, Vector2 _d, float _t)
    {
        float _t2 = _t * _t;
        float _t3 = _t2 * _t;
        return 0.5f * (
            (2f * _b) +
            (-_a + _c) * _t +
            (2f * _a - 5f * _b + 4f * _c - _d) * _t2 +
            (-_a + 3f * _b - 3f * _c + _d) * _t3
        );
    }

    private void EnsureCumulativeBufferSize(int _requiredCount)
    {
        if (cumulativeLengthBuffer.Length >= _requiredCount)
        {
            return;
        }

        int _newSize = cumulativeLengthBuffer.Length;
        while (_newSize < _requiredCount)
        {
            _newSize *= 2;
        }

        cumulativeLengthBuffer = new float[_newSize];
    }

    private void EnsureInputBufferSize(int _requiredCount)
    {
        if (inputPointsBuffer.Length >= _requiredCount)
        {
            return;
        }

        int _newSize = Mathf.Max(4, inputPointsBuffer.Length);
        while (_newSize < _requiredCount)
        {
            _newSize *= 2;
        }

        inputPointsBuffer = new Vector2[_newSize];
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        lineThickness = Mathf.Max(minLineThickness, lineThickness);
        axisMaxValue = Mathf.Max(MIN_AXIS_VALUE, axisMaxValue);
        samplesPerSegment = Mathf.Clamp(samplesPerSegment, 1, 30);
        miterLimit = Mathf.Clamp(miterLimit, 1f, 10f);
        Redraw();
    }
#endif
}
