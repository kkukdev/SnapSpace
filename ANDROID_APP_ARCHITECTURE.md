# 안드로이드 앱 아키텍처 상세 분석

## 목차
1. [전체 구조도](#전체-구조도)
2. [주요 컴포넌트](#주요-컴포넌트)
3. [데이터 흐름](#데이터-흐름)
4. [주요 기능별 플로우](#주요-기능별-플로우)
5. [함수 관계도](#함수-관계도)

---

## 전체 구조도

```
┌─────────────────────────────────────────────────────────────────┐
│                    안드로이드 앱 계층 구조                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │           UI 계층 (Activity & View)                      │   │
│  │  ┌────────────────────────────────────────────────────┐  │   │
│  │  │ Main Activity (메인 스캔 화면)                      │  │   │
│  │  │  - 스캔 실행/중지                                  │  │   │
│  │  │  - 센서 데이터 수신                                │  │   │
│  │  │  - 3D 뷰 렌더링                                   │  │   │
│  │  └────────────────────────────────────────────────────┘  │   │
│  │         ↓                                                  │   │
│  │  ┌─────────────┬──────────────┬──────────────────────┐   │   │
│  │  │   Editor    │ CameraControl│   DistanceMeasuring  │   │   │
│  │  │  (편집 UI)   │  (카메라 제어) │   (거리 측정 UI)    │   │   │
│  │  └─────────────┴──────────────┴──────────────────────┘   │   │
│  │         ↓                                                  │   │
│  │  ┌────────────────────────────────────────────────────┐  │   │
│  │  │ GLESSurfaceView (GL렌더링 뷰)                       │  │   │
│  │  │  - onDrawFrame: 프레임 렌더링                       │  │   │
│  │  │  - onSurfaceCreated: 초기화                        │  │   │
│  │  │  - onSurfaceChanged: 해상도 변경                   │  │   │
│  │  └────────────────────────────────────────────────────┘  │   │
│  └──────────────────────────────────────────────────────────┘   │
│                         ↓                                        │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │         비즈니스 로직 계층 (Controller)                    │   │
│  │  ┌─────────────────────────────────────────────────────┐ │   │
│  │  │ JNI (Java Native Interface)                         │ │   │
│  │  │  - Java ↔ C++ 통신                                 │ │   │
│  │  │  - 네이티브 메서드 호출                             │ │   │
│  │  │  - 이벤트 메시지 전송                               │ │   │
│  │  └─────────────────────────────────────────────────────┘ │   │
│  │         ↓                                                  │   │
│  │  ┌─────────────────────────────────────────────────────┐ │   │
│  │  │ Service (백그라운드 서비스)                          │ │   │
│  │  │  - 후처리 작업 (Postprocessing)                     │ │   │
│  │  │  - 저장 작업 (Save)                                │ │   │
│  │  │  - Sketchfab 업로드                                │ │   │
│  │  │  - 포토그래메트리                                   │ │   │
│  │  └─────────────────────────────────────────────────────┘ │   │
│  │         ↓                                                  │   │
│  │  ┌─────────────────────────────────────────────────────┐ │   │
│  │  │ Exporter (파일 내보내기)                            │ │   │
│  │  │  - OBJ 형식 변환                                    │ │   │
│  │  │  - PLY 형식 변환                                    │ │   │
│  │  │  - Dataset 형식 변환                                │ │   │
│  │  │  - 모델 압축                                        │ │   │
│  │  └─────────────────────────────────────────────────────┘ │   │
│  └──────────────────────────────────────────────────────────┘   │
│                         ↓                                        │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │          네이티브 계층 (C++)                             │   │
│  │  ┌──────────────────────────────────────────────────┐   │   │
│  │  │ ARCore/AREngine (AR 핵심 엔진)                   │   │   │
│  │  │  - Motion Tracking (모션 추적)                   │   │   │
│  │  │  - Plane Detection (평면 감지)                   │   │   │
│  │  │  - Point Cloud Processing                        │   │   │
│  │  └──────────────────────────────────────────────────┘   │   │
│  │         ↓                                                 │   │
│  │  ┌──────────────────────────────────────────────────┐   │   │
│  │  │ 3D 처리 모듈                                       │   │   │
│  │  │  - Reconstruction (재구성)                        │   │   │
│  │  │  - Texturize (텍스처링)                           │   │   │
│  │  │  - Mesh Processing                               │   │   │
│  │  │  - Point Cloud Optimization                       │   │   │
│  │  └──────────────────────────────────────────────────┘   │   │
│  │         ↓                                                 │   │
│  │  ┌──────────────────────────────────────────────────┐   │   │
│  │  │ 렌더링 엔진 (OpenGL ES)                            │   │   │
│  │  │  - Scene Management                              │   │   │
│  │  │  - Camera Control                                │   │   │
│  │  │  - Lighting & Material                           │   │   │
│  │  └──────────────────────────────────────────────────┘   │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
```

---

## 주요 컴포넌트

### 1. **Main Activity (main/Main.java)**

메인 엑티비티는 전체 앱의 중추로서 스캔, 렌더링, 사용자 입력을 관리합니다.

#### 주요 멤버 변수:
```java
private GLESSurfaceView mGLView;           // OpenGL 렌더링 뷰
private DistanceMeasuring mDistance;        // 거리 측정 뷰
private Editor mEditor;                     // 모델 편집 인터페이스
private CameraControl mCameraControl;       // 카메라 제어
private Indicators mIndicators;             // 상태 표시기 (배터리, 메모리)
private GPS mGPS;                           // GPS 좌표
private boolean m3drRunning = false;        // 스캔 실행 여부
```

#### 라이프사이클 메서드:

```
onCreate()
   ↓
onResume()
   ├─→ bindAR() (첫 실행시)
   │    └─→ JNI.onARServiceConnected()
   │         (AR 엔진 초기화)
   ↓
onDrawFrame() (렌더링 루프)
   ├─→ JNI.onGlSurfaceDrawFrame()
   ├─→ mCameraControl.updateMotion()
   └─→ mCameraControl.updateCapture()
   ↓
onPause()
   ├─→ JNI.onToggleButtonClicked(false)
   └─→ JNI.onPause()
   ↓
onBackPressed()
   └─→ System.exit(0)
```

---

### 2. **JNI 계층 (main/JNI.java)**

Java와 C++ 네이티브 코드 간의 상호작용을 담당합니다.

#### 핵심 네이티브 메서드:

```java
// AR 서비스 초기화
public static native boolean onARServiceConnected(Context context, double res, 
                                                   double dmin, double dmax, 
                                                   int noise, boolean holes, 
                                                   boolean poseCorr, 
                                                   boolean distortion, 
                                                   boolean offset, 
                                                   boolean flashlight, 
                                                   int mode, boolean clearing, 
                                                   byte[] temp);

// 렌더링 프레임
public static native boolean onGlSurfaceDrawFrame(boolean faceMode, 
                                                   float yaw, int viewMode, 
                                                   boolean anchors, boolean grid, 
                                                   boolean smooth);

// 토글 버튼 (녹화 시작/정지)
public static native void onToggleButtonClicked(boolean reconstructionRunning);

// 3D 모델 저장
public static native boolean save(byte[] name);

// 텍스처 적용
public static native void texturize(byte[] input, byte[] output, 
                                     boolean poisson, boolean twoPass);

// 모델 로드
public static native boolean load(byte[] name);

// 효과 적용 (색상 편집)
public static native void applyEffect(int effect, float value, int axis);

// 선택 작업
public static native void applySelect(float x, float y, boolean triangle);
public static native void circleSelection(float x, float y, float radius, 
                                          boolean invert);
public static native void rectSelection(float x1, float y1, float x2, float y2, 
                                        boolean invert);
```

#### 라이브러리 로드:
```java
static {
    System.loadLibrary("3dscanner");  // libvideo3dscanner.so 로드
}
```

---

### 3. **GLESSurfaceView 렌더러 (Main.java)**

OpenGL 렌더링을 담당하며 `GLESSurfaceView.Renderer` 인터페이스를 구현합니다.

#### 렌더링 사이클:

```
┌─ onSurfaceCreated() [최초 1회]
│   └─→ bindAR() 호출
│        (AR 엔진 초기화)
│
├─ onSurfaceChanged() [해상도 변경시]
│   ├─→ JNI.onGlSurfaceChanged(width, height, fullhd)
│   └─→ 녹음 바 크기 계산
│
└─ onDrawFrame() [매 프레임마다 60fps]
    ├─→ JNI.onGlSurfaceDrawFrame()  // C++ 렌더링
    │   └─→ 모션 추적, 메시 렌더링, 특징점 표시
    ├─→ JNI.didARjump() // 추적 끊김 감지
    ├─→ mCameraControl.updateCapture() // 카메라 제어 업데이트
    └─→ 화면에 렌더링
```

---

### 4. **Editor (main/Editor.java)**

3D 모델 편집 기능을 제공하는 UI입니다.

#### 편집 모드 상태도:

```
┌─────────────────┐
│   MAIN SCREEN   │ (메인 화면)
└────────┬────────┘
         │
    ┌────┴────┬─────────┬──────────────┐
    │          │         │              │
    ▼          ▼         ▼              ▼
┌────────┐ ┌──────┐ ┌────────┐    ┌────────┐
│ SELECT │ │COLOR │ │TRANSFORM│  │  EDIT  │
│  화면  │ │ 화면 │ │  화면   │  │  화면  │
└────────┘ └──────┘ └────────┘    └────────┘
    │          │         │              │
    │          │         │              │
    ▼          ▼         ▼              ▼
  선택    색상 조정   변형(이동/    undo/
  작업   (명도/      회전/확대)    복제/
        대비/채도)              삭제
```

#### 편집 기능:

```java
enum Effect { 
    CONTRAST,   // 명도 대비
    GAMMA,      // 감마
    SATURATION, // 채도
    TONE,       // 톤
    RESET,      // 초기화
    CLONE,      // 복제
    DELETE,     // 삭제
    MOVE,       // 이동
    ROTATE,     // 회전
    SCALE       // 크기
}
```

---

### 5. **CameraControl (main/CameraControl.java)**

3D 뷰의 카메라 조작을 관리합니다.

#### 뷰 모드:

```java
enum ViewMode { 
    UNKNOWN,      // 미정의
    FACE,         // 얼굴 모드 (셀카용)
    ORBIT,        // 궤도 모드 (회전)
    TOPDOWN,      // 위에서 본 뷰
    FIRST,        // 1인칭 뷰
    FLOORPLAN     // 바닥 평면도
}
```

#### 제스처 처리:

```
사용자 터치 입력
    │
    ├─ 싱글 탭 → 선택 (mSelection = true)
    ├─ 더블 탭 → 줌 인 (onDoubleClick)
    ├─ 드래그 → 카메라 회전
    ├─ 2 핑거 → 카메라 이동 또는 회전
    └─ 롱 클릭 → 선택 모드 활성화
```

---

### 6. **Service (ui/Service.java)**

백그라운드에서 실행되는 긴 작업을 처리합니다.

#### 서비스 상태:

```java
SERVICE_NOT_RUNNING = 0         // 미실행
SERVICE_POSTPROCESS = 1         // 후처리 중
SERVICE_SAVE = 2                // 저장 중
SERVICE_SKETCHFAB = 3           // Sketchfab 업로드 중
SERVICE_PHOTOGRAMMETRY = 4      // 포토그래메트리 중
```

#### 프로세스 플로우:

```
Service.process(message, serviceId, activity, runnable)
    │
    ├─→ SharedPreferences에 상태 저장
    ├─→ Intent로 Service 시작
    │
    └─→ Service.onCreate()
        ├─→ 상태에 따라 메시지 업데이트 스레드 시작
        └─→ runnable 실행
             (실제 작업: 후처리, 저장, 업로드 등)
```

---

### 7. **Indicators (main/Indicators.java)**

UI 상태 표시기입니다.

#### 표시 항목:

```
┌────────────────────────────────────────┐
│  [메모리 MB 수]     [배터리 %]  [배터리 아이콘]  │
│                                        │
│  [이벤트 로그 메시지]                  │
│   - ANALYSE: 특징점 분석               │
│   - FEW_FEATURES: 특징점 부족          │
│   - CONVERT: 변환 중                   │
│   - MERGE: 병합 중                     │
│   - MT_JUMP: 추적 끊김                 │
└────────────────────────────────────────┘
```

---

### 8. **DistanceMeasuring (main/DistanceMeasuring.java)**

두 점 사이의 거리를 측정하는 UI입니다.

#### 동작:

```
2개 이상의 터치 포인트 감지
    │
    ├─→ JNI.getDistance() 호출
    │    (두 점 사이의 3D 거리 계산)
    │
    └─→ Canvas에 화살표 그리기
        └─→ 거리 값 표시 (m 또는 cm)
```

---

### 9. **Exporter (main/Exporter.java)**

모델을 다양한 형식으로 내보냅니다.

#### 지원 형식:

```
- .dataset  : 프로젝트 파일 (모든 프레임 데이터 포함)
- .obj      : 3D 모델 파일 (텍스처 포함)
- .ply      : 포인트 클라우드 파일
```

#### 내보내기 프로세스:

```
export(file, filename)
    │
    ├─ OBJ 형식:
    │   ├─→ 텍스처 파일 이동
    │   ├─→ 메시 데이터 저장
    │   └─→ GPS 정보 복사
    │
    ├─ PLY 형식:
    │   ├─→ 포인트 클라우드 저장
    │   └─→ GPS 정보 복사
    │
    └─ Dataset 형식:
        └─→ 모든 프레임 데이터 저장
```

---

## 데이터 흐름

### 스캔 시작 프로세스

```
onClick(mToggleButton)
    │
    ├─→ m3drRunning = !m3drRunning
    │
    └─→ JNI.onToggleButtonClicked(m3drRunning)
         │
         ├─ C++에서:
         │  ├─→ 모션 추적 시작/정지
         │  ├─→ Point Cloud 획득 시작/정지
         │  └─→ 메시 생성 시작/정지
         │
         └─→ UI 업데이트
            └─→ mHandMotionView 표시/숨김
```

### 렌더링 사이클

```
onDrawFrame() [매 프레임]
    │
    ├─→ 텍스처링 확인
    │   (후처리 중이면 스킵)
    │
    ├─→ JNI.onGlSurfaceDrawFrame()
    │   ├─→ 카메라 행렬 계산
    │   ├─→ 포인트 클라우드 렌더링
    │   ├─→ 메시 렌더링
    │   ├─→ 특징점 표시
    │   └─→ 반환 값: true = 손 모션 감지됨
    │
    ├─→ JNI.didARjump() 확인
    │   (모션 추적이 끊겼으면 스캔 정지)
    │
    ├─→ 비디오 녹화 중이면:
    │   ├─→ Recorder.captureVideoFrame()
    │   ├─→ 카메라 회전 업데이트
    │   └─→ 360도 회전시 녹화 완료
    │
    └─→ 카메라 제어 업데이트
        └─→ 제스처 처리 결과 적용
```

### 저장 프로세스

```
save() 호출
    │
    ├─ 모드1: 얼굴 스캔 (Face Mode)
    │   ├─→ Service.forceState() 호출
    │   ├─→ JNI.save() (OBJ 저장)
    │   └─→ 후처리 서비스 시작
    │
    ├─ 모드2: 데이터셋 저장 (Dataset Mode)
    │   ├─→ Service.process() 호출
    │   ├─→ JNI.save() (모든 프레임 저장)
    │   └─→ 폴더를 .dataset으로 변환
    │
    └─ 모드3: OBJ 저장 (일반 모드)
        ├─→ Service.process() 호출
        ├─→ JNI.save() (메시 저장)
        ├─→ JNI.texturize() (텍스처 적용)
        └─→ 완료후 파일 경로 반환
```

### 후처리 프로세스

```
JNI.texturize(input, output, poisson, twoPass)
    │
    ├─→ C++ 텍스처링 알고리즘:
    │   ├─→ 카메라 이미지로부터 색상 추출
    │   ├─→ 메시에 색상 매핑
    │   └─→ Poisson 표면 재구성 (선택)
    │
    ├─→ 결과 저장
    │
    └─→ Service.finish() 호출
        └─→ 앱 종료
```

---

## 주요 기능별 플로우

### 1. **실시간 3D 스캔 플로우**

```
┌─────────────────────────────────────────────────────┐
│ User clicks "REC" button                             │
└──────────────────┬──────────────────────────────────┘
                   │
         onClick(mToggleButton)
                   │
    ┌──────────────┴──────────────┐
    │                             │
    ▼                             ▼
[m3drRunning=true]      [m3drRunning=false]
(스캔 시작)              (스캔 정지)
    │                             │
    ▼                             ▼
JNI.onToggleButtonClicked(true)  false
    │                             │
    ├─→ C++ AR엔진                ├─→ C++ 정지
    │   ├─ Point cloud 수집       │
    │   ├─ Feature tracking       │
    │   ├─ Mesh 생성              │
    │   └─ Color mapping          │
    │                             │
    └─→ 매 프레임 onDrawFrame()   └─→ UI 업데이트
        ├─ JNI.onGlSurfaceDrawFrame()
        ├─ 3D 메시 렌더링
        ├─ 손 모션 표시
        └─ 카메라 제어

(스캔 중)
  ↓
User clicks "SAVE" button
  ↓
save() 호출
  ↓
JNI.save() + JNI.texturize()
  ↓
Service로 후처리 진행
  ↓
파일 저장 완료
```

### 2. **모델 편집 플로우**

```
┌──────────────────────────────────┐
│ User loads a 3D model            │
└─────────────┬──────────────────────┘
              │
        setViewerMode()
              │
    ┌─────────┴─────────┐
    │                   │
    ▼                   ▼
[EDIT]              [EXPORT]
   │                   │
   ▼                   ▼
Editor.init()     compressModel()
   │                   │
   ├─→ setMainScreen() ├─→ ZIP 생성
   │   (5개 메뉴)      │
   │   │               ├─→ Sketchfab 업로드
   │   │               │
   │   ├─ SELECT       └─→ 혹은
   │   │   ├─ Select all/none
   │   │   ├─ Select by object
   │   │   ├─ Circle selection
   │   │   ├─ Rect selection
   │   │   └─ Increase/Decrease
   │   │
   │   ├─ COLOR
   │   │   ├─ Contrast 조정
   │   │   ├─ Gamma 조정
   │   │   ├─ Saturation 조정
   │   │   ├─ Tone 조정
   │   │   └─ Reset
   │   │
   │   ├─ TRANSFORM
   │   │   ├─ Move (X, Y, Z축)
   │   │   ├─ Rotate (X, Y, Z축)
   │   │   └─ Scale (균등)
   │   │
   │   ├─ EDIT
   │   │   ├─ Restore (이전 상태)
   │   │   ├─ Clone (복제)
   │   │   ├─ Delete (삭제)
   │   │   └─ Toggle Normals (법선 표시)
   │   │
   │   └─ EXIT (저장)
   │       │
   │       └─→ Filename 입력
   │           ↓
   │       JNI.saveWithTextures()
   │           ↓
   │       파일 저장
   │
   ▼
[SeekBar로 값 조정]
   │
   ├─→ SeekBar 이벤트
   ├─→ JNI.previewEffect()
   │   (실시간 미리보기)
   │
   └─→ 버튼 클릭시 적용
       └─→ JNI.applyEffect()
```

### 3. **터치 제스처 처리 플로우**

```
onTouch(View v, MotionEvent event)
    │
    ├─ 에디터 모드 확인
    │  └─→ 에디터가 터치 처리
    │
    ├─ ACTION_DOWN (터치 시작)
    │  ├─→ 더블 탭 감지 (<500ms)
    │  │   └─→ mCameraControl.onDoubleClick()
    │  │       (줌인)
    │  │
    │  └─→ 롱 클릭 감지 시작
    │      └─→ 500ms 후 mSelection = true
    │
    ├─ ACTION_MOVE (드래그)
    │  └─→ mCameraControl.updateMotion()
    │      ├─→ GestureDetector.OnDrag()
    │      └─→ JNI.setView() 업데이트
    │
    └─ ACTION_UP (터치 끝)
       ├─→ 이동 > 5% → 롱클릭 취소
       │
       ├─→ 짧은 클릭 (<500ms)
       │   ├─→ mSelection이 true면
       │   │   └─→ JNI.completeSelection(true)
       │   │       (선택된 객체 확정)
       │   │
       │   └─→ 아니면 카메라 뷰 전환
       │
       └─→ mCameraControl.updateCapture()

[GestureDetector 상세]
    │
    ├─ IsAcceptingRotation()
    │  └─→ TopDown or FloorPlan 모드면 true
    │
    ├─ OnDrag(dx, dy)
    │  ├─→ 회전 가능 모드:
    │  │   └─→ 회전된 좌표계에서 이동
    │  │
    │  └─→ Face/First/Orbit 모드:
    │      ├─→ Pitch 조정 (위/아래)
    │      ├─→ Yaw 조정 (좌/우)
    │      └─→ JNI.setView() 호출
    │
    └─ OnTwoFingerMove(dx, dy)
       └─→ 2손가락 드래그
           ├─→ Zoom 조정 (mMoveZ)
           └─→ Pan 조정 (mMoveX, mMoveY)
```

---

## 함수 관계도

### A. 스캔 관련 함수 호출 체인

```
Main.onClick(toggleButton)
  ├─→ m3drRunning = !m3drRunning
  └─→ JNI.onToggleButtonClicked(m3drRunning)
       ├─→ [C++] AREngine::startTracking()
       │   ├─→ camera.start()
       │   ├─→ pointCloud.start()
       │   └─→ mesh.start()
       │
       └─→ RunOnUiThread()
           └─→ mHandMotionView.setVisibility()

Main.onDrawFrame(GL10 gl)
  ├─→ JNI.onGlSurfaceDrawFrame(...)
  │   ├─→ [C++] ARCore::Process()
  │   │   ├─→ captureFrame()
  │   │   ├─→ trackMotion()
  │   │   ├─→ extractFeatures()
  │   │   ├─→ createPointCloud()
  │   │   └─→ generateMesh()
  │   │
  │   └─→ [C++] Renderer::render()
  │       ├─→ drawPointCloud()
  │       ├─→ drawMesh()
  │       └─→ drawFeaturePoints()
  │
  ├─→ JNI.didARjump()
  │   ├─→ [C++] ARCore::trackingJumped()
  │   └─→ true이면 스캔 중지
  │
  └─→ CameraControl.updateCapture(gl, view)
      └─→ Recorder.captureVideoFrame() (비디오 녹화중)
```

### B. 저장 관련 함수 호출 체인

```
Main.save()
  │
  ├─ Face Mode:
  │  ├─→ JNI.onToggleButtonClicked(false)  // 스캔 정지
  │  ├─→ JNI.save(path)                    // OBJ 저장
  │  │   ├─→ [C++] Mesh::save()
  │  │   ├─→ [C++] Exporter::exportOBJ()
  │  │   └─→ true 반환
  │  │
  │  └─→ Service.forceState(path, POSTPROCESS)
  │      └─→ 후처리 서비스 시작
  │
  ├─ Dataset Mode:
  │  └─→ Service.process(message, SERVICE_SAVE, callback)
  │      └─→ Service.onCreate()
  │          ├─→ 이벤트 업데이트 스레드 시작
  │          │   ├─→ JNI.getEvent() 루프
  │          │   └─→ 1초마다 업데이트
  │          │
  │          └─→ 콜백 실행:
  │              ├─→ JNI.save(path)
  │              ├─→ 폴더를 .dataset으로 변환
  │              └─→ Service.finish(path)
  │
  └─ Normal Mode:
     └─→ Service.process(message, SERVICE_SAVE, callback)
         └─→ 콜백 실행:
             ├─→ JNI.save(input_path)
             │   └─→ [C++] Mesh 저장
             │
             ├─→ JNI.texturize(input, output, poisson, twoPass)
             │   └─→ [C++] Texturizer::process()
             │       ├─→ extractTextureCoordinates()
             │       ├─→ mapCameraColors()
             │       ├─→ poissonReconstruction() (선택)
             │       └─→ save(output)
             │
             └─→ Service.finish(output_path)
                 └─→ System.exit(0)
```

### C. 편집 관련 함수 호출 체인

```
Main.setViewerMode()
  └─→ mEditorButton.setOnClickListener()
      └─→ Editor.init()
          ├─→ setMainScreen()
          ├─→ SeekBar.setOnSeekBarChangeListener()
          │   └─→ onProgressChanged():
          │       └─→ JNI.previewEffect(effect, value, axis)
          │           └─→ [C++] Model::applyEffect(preview)
          │
          └─→ 초기 선택:
              └─→ JNI.completeSelection(mComplete)
                  └─→ [C++] Model::selectAll()

Editor.onClick(button)
  │
  ├─ SELECT 메뉴:
  │  ├─→ "select all"
  │  │   └─→ JNI.completeSelection(!mComplete)
  │  │       └─→ [C++] Model::selectAll() or deselectAll()
  │  │
  │  ├─→ "select object"
  │  │   └─→ touchEvent() 대기
  │  │       └─→ JNI.applySelect(x, y, false)
  │  │           └─→ [C++] Model::selectByRay()
  │  │
  │  ├─→ "circle select"
  │  │   └─→ touchEvent() (드래그로 원 그리기)
  │  │       └─→ JNI.circleSelection(x, y, radius, invert)
  │  │           └─→ [C++] Model::selectByCircle()
  │  │
  │  └─→ "rect select"
  │      └─→ touchEvent() (드래그로 사각형 그리기)
  │          └─→ JNI.rectSelection(x1, y1, x2, y2, invert)
  │              └─→ [C++] Model::selectByRect()
  │
  ├─ COLOR 메뉴:
  │  ├─→ "contrast", "gamma", "saturation", "tone"
  │  │   ├─→ showSeekBar(false)
  │  │   └─→ SeekBar 리스너:
  │  │       └─→ JNI.previewEffect(effect, value, 0)
  │  │
  │  └─→ "reset"
  │      └─→ JNI.applyEffect(RESET, 0, 0)
  │          └─→ [C++] Model::resetColors()
  │
  ├─ TRANSFORM 메뉴:
  │  ├─→ "move"
  │  │   ├─→ showSeekBar(true)
  │  │   └─→ 축 선택 (X, Y, Z)
  │  │       └─→ SeekBar:
  │  │           └─→ JNI.previewEffect(MOVE, value, axis)
  │  │
  │  ├─→ "rotate"
  │  │   └─→ JNI.previewEffect(ROTATE, value, axis)
  │  │
  │  └─→ "scale"
  │      └─→ JNI.previewEffect(SCALE, value, 0)
  │
  ├─ EDIT 메뉴:
  │  ├─→ "restore"
  │  │   └─→ JNI.restore()
  │  │       └─→ [C++] Model::undo()
  │  │
  │  ├─→ "clone"
  │  │   └─→ JNI.applyEffect(CLONE, 0, 0)
  │  │       └─→ [C++] Model::cloneSelection()
  │  │
  │  └─→ "delete"
  │      └─→ JNI.applyEffect(DELETE, 0, 0)
  │          └─→ [C++] Model::deleteSelection()
  │
  └─ "save"
     ├─→ 파일명 입력 다이얼로그
     └─→ Editor.save():
         ├─→ JNI.saveWithTextures(path)
         │   └─→ [C++] Model::save()
         │
         └─→ 파일 이동
             └─→ mContext.runOnUiThread()
```

### D. 카메라 제어 함수 호출 체인

```
Main.onTouch(v, event)
  └─→ mCameraControl.updateMotion(event)
      └─→ mGestureDetector.onTouch(event)
          ├─→ ACTION_DOWN
          │   ├─→ saveLastPointer()
          │   └─→ startLongClick() (타이머 시작)
          │
          ├─→ ACTION_MOVE
          │   ├─→ calculateDelta()
          │   ├─→ if (큰 이동):
          │   │   └─→ cancelLongClick()
          │   │
          │   └─→ OnDrag(dx, dy) 콜백
          │       └─→ CameraControl.OnDrag()
          │           ├─→ 회전 모드:
          │           │   └─→ JNI.setView(yaw, pitch, x, y, z, o, gyro)
          │           │
          │           └─→ Orbit 모드:
          │               └─→ mPitch, mYawM 업데이트
          │                   └─→ JNI.setView(...)
          │
          └─→ ACTION_UP
              ├─→ 500ms 이내 + 작은 이동:
              │   ├─→ if (mSelection):
              │   │   └─→ JNI.completeSelection(true)
              │   │
              │   └─→ 아니면 카메라 뷰 전환
              │
              └─→ 다른 경우: 롱클릭 취소

CameraControl.onDoubleClick(move)
  ├─→ move < 0.05f (작은 이동) 확인
  └─→ mCameraControl.zoom()
      ├─→ mMoveZ 조정
      └─→ JNI.setView(...)

CameraControl.updateButtons()
  ├─→ JNI.animFinished() 확인
  ├─→ JNI.getView(axis) 조회
  └─→ 뷰 정보 출력
```

---

## 상태 변화도

### 앱 전체 상태 다이어그램

```
                           ┌─────────────────┐
                           │   APP_START     │
                           └────────┬────────┘
                                    │
                    onCreate() + onResume()
                                    │
                                    ▼
                    ┌───────────────────────────────┐
                    │  PERMISSION_CHECK / INIT      │
                    │  ├─ AR 서비스 확인            │
                    │  ├─ 저장소 권한 확인          │
                    │  └─ 센서 초기화               │
                    └───┬───────────────────────────┘
                        │
        ┌───────────────┼───────────────┐
        │               │               │
        ▼               ▼               ▼
    ┌────────┐  ┌──────────┐  ┌────────────────┐
    │SCAN    │  │VIEW      │  │POSTPROCESS     │
    │MODE    │  │MODE      │  │(SERVICE)       │
    └────────┘  └──────────┘  └────────────────┘
        │           │                   │
        │           └───┬───────────────┘
        │               │
        │       ┌───────▼──────────┐
        │       │  EDIT_MODE       │
        │       │  ├─ SELECT       │
        │       │  ├─ COLOR        │
        │       │  ├─ TRANSFORM    │
        │       │  ├─ EDIT         │
        │       │  └─ EXIT         │
        │       └───────┬──────────┘
        │               │
        └───────┬───────┴──────────┬────────────┐
                │                  │            │
                ▼                  ▼            ▼
          ┌──────────┐        ┌──────────┐  ┌──────────┐
          │SAVE OBJ  │        │SAVE DATA │  │SAVE FACE │
          └─────┬────┘        └────┬─────┘  └────┬─────┘
                │                  │             │
                └──────────┬───────┴─────────────┘
                           │
                        Save()
                           │
                  ┌────────┬┴────────┐
                  │        │        │
                  ▼        ▼        ▼
            ┌─────────┐ ┌──────────┐
            │TEXTURIZE│ │OPTIMIZE  │
            │SERVICE  │ │SERVICE   │
            └────┬────┘ └────┬─────┘
                 │           │
                 └─────┬─────┘
                       │
                ┌──────▼────────┐
                │EXPORT_SERVICE │
                └────────┬──────┘
                         │
                    ┌────▼─────┐
                    │           │
                    ▼           ▼
               ┌─────────┐  ┌─────────────┐
               │ FINISH  │  │SKETCHFAB    │
               │SUCCESS  │  │UPLOAD       │
               └────┬────┘  └──────┬──────┘
                    │             │
                    └──────┬──────┘
                           │
                           ▼
                    ┌────────────────┐
                    │  System.exit(0)│
                    └────────────────┘
```

---

## 메모리 및 성능 고려사항

### 1. 메모리 관리

```java
// 1. Point Cloud 저장
LinkedList<glm::vec3> points      // 매 프레임마다 추가
  → 대량의 3D 좌표 저장

// 2. 메시 데이터
Mesh mesh                          // 동적으로 증가
  ├─ 정점 (vertices)
  ├─ 법선 (normals)
  ├─ 텍스처 좌표 (UV)
  └─ 인덱스 (indices)

// 3. 텍스처
Texture[] textures                 // 카메라 프레임마다 생성
  └─ GPU 메모리 할당

// Indicators에서 메모리 모니터링
int freeMBs = mMemoryInfo.availMem / 1048576L
if (freeMBs < 400)
    WARNING 표시
```

### 2. 성능 최적화

```java
// 1. 해상도 설정
mRes = getResolution(this)         // 자동 계산
if (mRes > 0.0099f) {
    mCameraControl.setOffset(mRes * 100)
}

// 2. 소수점 단위 제어
decimation = Integer.parseInt(...) // 1, 2, 4 등
texture_max = Math.min(Math.max(1, MB / 512), 8)
texture_res = 2048                 // 텍스처 해상도

// 3. Full HD 모드
boolean fullhd = pref.getBoolean(pref_fullhd, false)
JNI.onGlSurfaceChanged(width, height, fullhd)

// 4. 렌더링 최적화
boolean smooth = !mRecording       // 비디오 녹화중은 비활성화
JNI.onGlSurfaceDrawFrame(..., smooth)
```

---

## 요약: 앱 동작 시나리오

### 전형적인 사용 시나리오

```
1. 앱 시작
   └─→ onCreate() 
       └─→ UI 초기화
       └─→ GLESSurfaceView 설정
       └─→ onResume()
           └─→ bindAR()
               └─→ JNI.onARServiceConnected()

2. 사용자 "REC" 클릭 (스캔 시작)
   └─→ onClick(mToggleButton)
       └─→ m3drRunning = true
       └─→ JNI.onToggleButtonClicked(true)
           └─→ C++ AR엔진 시작
       └─→ mHandMotionView 표시

3. 렌더링 루프 (매 프레임)
   └─→ onDrawFrame()
       └─→ JNI.onGlSurfaceDrawFrame()
       └─→ 포인트 클라우드 + 메시 렌더링
       └─→ 카메라 제어 업데이트

4. 사용자 "PAUSE" 클릭 (스캔 정지)
   └─→ m3drRunning = false
   └─→ JNI.onToggleButtonClicked(false)

5. 사용자 "SAVE" 클릭
   └─→ save()
   └─→ Service.process()로 백그라운드 작업 시작
   └─→ JNI.texturize()
   └─→ 파일 저장

6. 앱 종료
   └─→ System.exit(0)
```

### 편집 모드 시나리오

```
1. 기존 모델 열기
   └─→ 파일 선택
   └─→ setViewerMode()
   └─→ JNI.load(filename)

2. 편집 버튼 클릭
   └─→ Editor.init()
   └─→ 메인 화면 표시 (5개 메뉴)

3. SELECT → 삼각형 선택
   └─→ JNI.applySelect(x, y)
   └─→ 선택된 부분 하이라이트

4. COLOR → 명도 조정
   └─→ SeekBar 조정
   └─→ JNI.previewEffect()로 미리보기
   └─→ 확인하면 JNI.applyEffect()

5. SAVE
   └─→ 파일명 입력
   └─→ JNI.saveWithTextures()
   └─→ 변경사항 저장
```

---

이 문서는 SpaceSnap 안드로이드 앱의 전체 아키텍처를 상세히 분석합니다. 
앱은 **3단계 계층 구조**(UI → 비즈니스 로직 → 네이티브 C++)로 설계되어 있으며,
**JNI를 통한 Java-C++ 상호작용**이 핵심입니다.
