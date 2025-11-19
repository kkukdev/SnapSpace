# OBJ Drop Watcher – User Guide

이 문서는 Unity 프로젝트 `Tool` 폴더 내에 포함된 **OBJ Drop Watcher** 에디터 툴과 메모 패널 기능을 사용하는 방법을 정리한 가이드입니다.

## 1. 준비 사항

1. **WatchConfig 에셋 생성**
   - `Project` 창에서 `Create > Configs > WatchConfig` 선택.
   - 생성한 에셋을 적절한 폴더(예: `Assets/Settings`)에 저장.
   - 에셋을 선택하고 Inspector에서 다음 항목을 설정:
     - `apiServerUrl`: 백엔드 API 기본 URL (예: `http://localhost:8000`).
     - 필요 시 `projectRoot`, `objPatterns`, `unitScale` 등도 입력.

2. **SnapSpace 폴더**
   - 툴이 자동으로 `Assets/SnapSpace/<GroupName>` 구조를 생성하며,
   - `.gitignore`에 의해 통째로 제외되므로 원한다면 수동 백업 필요.

## 2. OBJ Drop Watcher 열기

1. Unity 메뉴에서 `Tools > OBJ Drop Watcher` 실행.
2. 창 상단의 `WatchConfig` 슬롯에 앞에서 만든 에셋을 할당.

## 3. API 연동 및 그룹/스캔 조회

1. `Server URL` 입력 → 올바른 HTTP/HTTPS 문자열인지 확인.
2. `그룹 목록 새로고침` 버튼을 눌러 서버에서 그룹 목록을 가져옴.
3. 원하는 그룹을 선택한 뒤 `스캔 데이터 조회`를 클릭하면, 해당 그룹의 스캔/메시 파일 정보가 리스트로 표시됨.

## 4. 스캔 Import & Prefab 저장 흐름

1. 스캔 항목에서 `Import` 버튼을 누르면:
   - Original/Retouched OBJ 파일을 로드 (필요 시 GLB/FBX도 지원).
   - `storage` 경로의 `memos.json`을 찾아 메모를 함께 생성.
   - `Assets/SnapSpace/<GroupName>/prefabs/<ScanId>_Root.prefab`으로 저장 (이미 존재하면 자동 재사용).
   - 씬에는 Prefab 인스턴스가 배치되고, 메모가 다시 붙음.
2. Prefab에는 메모 본체가 포함되지 않으므로, 인스턴스 상태에서만 메모가 나타남.

## 5. 메모 패널 설정

### 5.1 Watcher UI에서 전역 Y 고정

Watcher 창 `Settings > Memo Panel` 섹션에서 다음을 제어할 수 있습니다.

| 옵션 | 설명 |
| --- | --- |
| `Lock Panel World Y` | 켜면 모든 메모 패널의 월드 Y 높이를 고정. |
| `Fixed Panel World Y` | 앞 옵션이 켜진 경우 사용할 고정 Y 값. |

설정 변경 시 `MemoUtils`의 디자인 설정이 업데이트되며 이후 생성되는 메모에 즉시 반영됩니다.

### 5.2 씬에서 개별 Y 오버라이드

- `MemoPanelHeightOverride` 컴포넌트를 부모 계층(예: Prefab 루트)에 붙여 `targetWorldY`를 지정하면, 해당 하위 메모만 별도 높이를 사용할 수 있습니다.
- `applyOverride`를 끄면 Watcher UI 또는 기본 설정 값이 사용됩니다.

## 6. 메모 패널 동작 정리

1. **XZ 평면 정면 고정**  
   - 패널은 생성 시 항상 월드 축 기준 `(X=90°, Y=0°, Z=0°)` 회전을 기본으로 사용하여 정면을 유지합니다.
2. **Parent 회전 보정**  
   - `MemoPanelHeightLock`가 parent의 회전을 역으로 적용하므로, 부모가 회전해도 패널은 월드 기준으로 똑바르게 유지됩니다.
3. **수동 회전**  
   - 사용자가 패널 Transform의 Rotation을 직접 수정하면 그 값을 유지하며, 이후에도 parent 회전만 보정합니다.

## 7. 기타 유용한 구성 요소

| 컴포넌트 | 용도 |
| --- | --- |
| `MemoPanelHeightLock` | 패널의 월드 XYZ 위치/회전을 유지. 자동으로 붙음. |
| `MemoPanelHeightOverride` | 특정 부모 아래 메모의 고정 Y값을 Inspector에서 지정. |
| `MemoLineConnector` | 마커와 패널을 Cylinder로 연결; parent 회전에 대응. |

## 8. 문제 해결 팁

- **Material이 Missing으로 바뀌는 경우**  
  - 기존 Prefab을 먼저 재사용하도록 수정되어 있으므로, 그래도 문제가 있다면 SnapSpace 폴더 내부 Asset을 점검.
- **메모가 잘못된 높이에 생성되는 경우**  
  - Watcher UI의 `Memo Panel` 섹션이나 `MemoPanelHeightOverride` 설정을 확인.
- **HTTP API 요청 실패**  
  - Unity 2021.2 이상에서는 HTTP가 차단될 수 있으므로 `Edit > Project Settings > Player > Allow downloads over HTTP`를 활성화하거나 HTTPS 서버 사용.

## 9. 정리

1. WatchConfig를 설정하고 OBJ Drop Watcher를 연다.
2. API 서버에서 그룹/스캔을 조회한다.
3. Import 시 SnapSpace 폴더에 Prefab과 Material이 저장되고, 씬에는 Prefab 인스턴스가 배치된다.
4. Watcher UI 또는 씬 컴포넌트로 메모 패널의 고정 높이와 회전을 제어한다.

필요 시 본 README를 프로젝트 내 다른 팀원들에게 공유하여 동일한 절차로 사용할 수 있도록 한다.


