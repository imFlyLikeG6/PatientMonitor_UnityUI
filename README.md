# PatientMonitor_UnityUI
ECG, SpO2, BPCuff Monitor graph

Unity UGUI 기반으로 만든 환자 모니터 그래프 시스템입니다.
ECG / Pleth / RR 형태의 파형을 실시간 스캔 방식으로 렌더링할 수 있습니다.

![PatientMonitor](https://github.com/user-attachments/assets/f218ed8c-7dd7-4f93-9b2b-e17e10ae4090)

## Features

- UGUI `Graphic` 기반 커스텀 라인 렌더러 (`LineGraph`)
- 점 배열(`Vector2[]`) 기반 라인/곡선 렌더링
- `MonitorGraph`의 2중 버퍼 + 마스크(fill) 방식 스캔 연출
- `GraphData` (`ScriptableObject`) 기반 파형 데이터 관리
- `PatientMonitor`에서 연결 상태/파형/레이트/BP 통합 관리
- 인스펙터 디버그 Connect 토글 지원

---

## Components Overview

### `LineGraph`
- UGUI 메쉬를 직접 생성하는 라인 컴포넌트
- 좌표계: `x: 0~100`, `y: 0~100`
- 곡선 옵션(Catmull-Rom), 두께, miter join 등 지원
- `SetData(Vector2[])`, `SetData(List<Vector2>)` 지원

### `MonitorGraph`
- 그래프 스캔 동작(진행 위치, 파형 생성, 버퍼 스위칭) 담당
- `GraphData`를 읽어 현재 구간 파형을 생성
- `Connect == false`일 때 기본 baseline(`NONE_VALUE`) 표시
- `currentPosition` 추적 오브젝트(`positionFollower`) 지원

### `PatientMonitor`
- 여러 `MonitorGraph`를 한 번에 제어하는 매니저
- 타입:
  - `PatientMonitorGraphType` (`ECG_II`, `Pleth`, `RR`)
  - `ConnectType` (`ECG`, `SpO2`, `BPCuff`)
  - `PatientDataType` (그래프 이름, rate, BP 등)
- 디버그용 connect bool 토글 지원

### `GraphData` (`ScriptableObject`)
- 파형 샘플 목록/주기/wave_count/random 범위 정의

---

## Folder Structure (example)

```text
Assets/
  PatientMonitor/
    03_Script/
      LineGraph.cs
      MonitorGraph.cs
      PatientMonitor.cs
      GraphData.cs
```

---

## Quick Start

1. Canvas 하위에 그래프 UI 구성
2. 그래프 라인 오브젝트에 `LineGraph` 추가
3. `MonitorGraph` 오브젝트에 아래 연결
   - `chartMask` 2개
   - `lineGraph` 2개
   - `graphData` 지정
4. 상위 매니저에 `PatientMonitor` 추가 후 `graphGroups` 연결
5. 런타임에서 connect/data 설정
   
<img width="2360" height="685" alt="그림1" src="https://github.com/user-attachments/assets/69769369-bef5-4e35-bd30-049449e56f3d" />

예시:
```csharp
patientMonitor.SetConnect(PatientMonitor.ConnectType.ECG, true);
patientMonitor.SetPatientData(PatientMonitor.PatientDataType.ECG_GRAPH, "ecg_normal");
patientMonitor.SetPatientData(PatientMonitor.PatientDataType.HR_MIN, "60");
patientMonitor.SetPatientData(PatientMonitor.PatientDataType.HR_MAX, "100");
```

---

## Blood Pressure Display

`PatientMonitor.UpdateBP()`는 다음 형식으로 표시합니다:

- `SBP/DBP (MAP)`

`MAP` 계산식:
- `MAP = (SBP + 2 * DBP) / 3` (반올림 정수)
