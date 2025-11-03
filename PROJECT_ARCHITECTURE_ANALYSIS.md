# 3D Live Scanner 프로젝트 아키텍처 분석

## 📋 목차
1. [프로젝트 개요](#프로젝트-개요)
2. [디렉토리 구조](#디렉토리-구조)
3. [핵심 아키텍처](#핵심-아키텍처)
4. [모듈별 기능](#모듈별-기능)
5. [기능 흐름도](#기능-흐름도)
6. [클래스 다이어그램](#클래스-다이어그램)

---

## 프로젝트 개요

**3D Live Scanner**는 Android 기반의 AR 3D 스캔 애플리케이션입니다.
- **언어**: Java (Android UI), C++ (Core 3D Processing)
- **AR 엔진**: Google ARCore (Primary), Huawei AREngine (Alternative)
- **3D 처리**: Tango 3D Reconstruction API
- **목표**: 실시간 3D 스캔, 편집, 내보내기

---

## 디렉토리 구조

```
App/
├── arcore/              # ARCore 바이너리 및 헤더
│   ├── include/
│   └── jni/             # 기기별 native 라이브러리
│
├── common/              # C++ 핵심 로직
│   ├── ar/              # Java AR 유틸리티
│   ├── arcore/          # ARCore 래퍼
│   │   ├── arcore.h/cc
│   │   ├── arengine.h/cc
│   │   ├── camera.h/cc
│   │   └── service.h/cc
│   │
│   ├── data/            # 데이터 구조 및 처리
│   │   ├── dataset.h/cc
│   │   ├── depthmap.h/cc
│   │   ├── image.h/cc
│   │   └── mesh.h/cc
│   │
│   ├── editor/          # 3D 편집 모듈
│   │   ├── effector.h/cc
│   │   ├── rasterizer.h/cc
│   │   └── selector.h/cc
│   │
│   ├── exporter/        # 포맷 변환 및 내보내기
│   │   ├── exporter.h/cc
│   │   ├── ply.h/cc
│   │   ├── floorpln.h/cc
│   │   └── csvposes.h/cc
│   │
│   ├── gl/              # OpenGL 렌더링
│   │   ├── renderer.h/cc
│   │   ├── camera.h/cc
│   │   ├── scene.h/cc
│   │   └── glsl.h/cc
│   │
│   ├── postproc/        # 후처리
│   │   ├── optimizer.h/cc
│   │   ├── poisson.h/cc
│   │   └── texturize.h/cc
│   │
│   ├── thread/          # 멀티스레딩 처리
│   │   ├── reconstr.h/cc    # 메인 재구성 엔진
│   │   └── scene.h/cc
│   │
│   ├── tango/           # Tango 3D Reconstruction
│   │   ├── retango.h/cc
│   │   ├── scan.h/cc
│   │   └── texturize.h/cc
│   │
│   └── utils/           # 유틸리티
│
├── scanner/             # Android Gradle 프로젝트
│   ├── app/src/main/
│   │   ├── java/com/snapspace/scanner/
│   │   │   ├── core/           # (현재 비어있음)
│   │   │   ├── main/           # 메인 스캔 로직
│   │   │   │   ├── Main.java
│   │   │   │   ├── CameraControl.java
│   │   │   │   ├── Editor.java
│   │   │   │   ├── Exporter.java
│   │   │   │   ├── JNI.java
│   │   │   │   ├── DistanceMeasuring.java
│   │   │   │   ├── HandMotionView.java
│   │   │   │   └── Indicators.java
│   │   │   │
│   │   │   ├── ui/             # UI 및 파일 관리
│   │   │   │   ├── HomeActivity.java
│   │   │   │   ├── FileManager.java
│   │   │   │   ├── Service.java (Background Service)
│   │   │   │   ├── AbstractActivity.java
│   │   │   │   ├── Settings.java
│   │   │   │   └── Common UI Utilities
│   │   │   │
│   │   │   └── sketchfab/      # 3D 모델 업로드
│   │   │
│   │   └── res/                # XML 리소스
│   │
│   ├── build.gradle
│   └── gradle.properties
│
└── third_party/         # 외부 라이브러리
    ├── glm/             # GLM 수학 라이브러리
    ├── delaunay/        # Delaunay 삼각분할
    ├── glm/
    ├── libjpeg-turbo/
    ├── libpng/
    ├── opencv/          # 이미지 처리 (특징 추출)
    └── poisson/         # Poisson 표면 재구성
```

---

## 핵심 아키텍처

### 계층 구조 (Layer Architecture)

```
┌─────────────────────────────────────┐
│     User Interface Layer (Android)  │
│  HomeActivity → Main → FileManager  │
└──────────────┬──────────────────────┘
               │ JNI (Native Interface)
┌──────────────▼──────────────────────┐
│    Android Wrapper Layer (C++)      │
│  ARCoreService → Reconstruction     │
└──────────────┬──────────────────────┘
               │
┌──────────────▼──────────────────────┐
│     Core Processing Layer (C++)     │
│  Tango3D ← Dataset → GL Renderer    │
│  ARCore ← Camera → Mesh Processing  │
└──────────────┬──────────────────────┘
               │
┌──────────────▼──────────────────────┐
│      Graphics & Data Layer (C++)    │
│  OpenGL → GLRenderer → Mesh/Image   │
└─────────────────────────────────────┘
```

### 핵심 컴포넌트 관계도

```mermaid
graph TB
    subgraph UI["Android UI Layer"]
        HA["HomeActivity<br/>(Start Screen)"]
        MA["Main Activity<br/>(Scanning)"]
        FM["FileManager<br/>(File Management)"]
        SA["Settings<br/>(Configuration)"]
    end
    
    subgraph Service["Service Layer"]
        Svc["BackgroundService<br/>(Async Processing)"]
    end
    
    subgraph JNI["JNI Bridge"]
        JNI_API["JNI.java<br/>(Native Calls)"]
    end
    
    subgraph C++ ["C++ Core Layer"]
        ARS["ARCoreService<br/>(AR Engine)"]
        Recon["Reconstruction<br/>(3D Processing)"]
        DS["Dataset<br/>(Data Management)"]
        Scene["Scene<br/>(3D Scene)"]
    end
    
    subgraph Processing["Post-Processing"]
        Exp["Exporter<br/>(Format Conversion)"]
        Tex["Texturize<br/>(Texture Mapping)"]
        Opt["Optimizer<br/>(Mesh Optimization)"]
    end
    
    HA -->|스캔 시작| MA
    MA -->|저장 요청| Svc
    MA -->|파일 관리| FM
    MA -->|설정 변경| SA
    
    MA -->|JNI Call| JNI_API
    FM -->|JNI Call| JNI_API
    
    JNI_API -->|ARCore Init| ARS
    JNI_API -->|Start/Stop| Recon
    
    ARS -->|Camera/Pose| Recon
    Recon -->|Save/Load| DS
    Recon -->|Render| Scene
    
    Svc -->|Texturize| Tex
    Svc -->|Optimize| Opt
    Svc -->|Export| Exp
```

---

## 모듈별 기능

### 1️⃣ **UI Layer** (Java - Android)

#### HomeActivity
- **역할**: 애플리케이션 시작 화면
- **기능**:
  - 공간 스캔 모드 선택
  - 오브젝트 스캔 모드 선택
  - 스캔 미리보기 기능
  - 서버 업로드 버튼
  - 권한 확인 및 요청

#### Main Activity
- **역할**: 메인 스캔 인터페이스 (가장 복잡한 UI)
- **주요 컴포넌트**:
  - `GLESSurfaceView`: OpenGL 렌더링 화면
  - `CameraControl`: 카메라 뷰 제어
  - `Editor`: 3D 모델 편집
  - `DistanceMeasuring`: 거리 측정
  - `Indicators`: 상태 표시
- **기능**:
  - 실시간 AR 스캔
  - 토글 버튼 (Record/Pause)
  - Clear (스캔 초기화)
  - Undo (되돌리기)
  - View Mode (뷰 전환)
  - Save (저장)
  - Editor 모드 (3D 편집)
  - Photo Mode (사진 캡처)
  - GPS 좌표 기록

#### FileManager
- **역할**: 저장된 파일 관리
- **기능**:
  - 파일 목록 조회 (GridView)
  - 파일 미리보기
  - 파일 삭제/이름 변경
  - 파일 공유 (Sketchfab)
  - 위치 정보 관리

#### Settings
- **역할**: 애플리케이션 설정
- **주요 설정**:
  - AR 모드 선택 (Google vs Huawei)
  - Face Mode 토글
  - ToF 카메라 사용 여부
  - 노이즈 필터 강도
  - 텍스처 해상도
  - GPU 메모리 할당
  - Poisson 표면 재구성 여부

### 2️⃣ **Service Layer** (Background Processing)

#### BackgroundService (Service.java)
```
역할: 비동기 작업 수행
상태 관리:
  - SERVICE_NOT_RUNNING (0)
  - SERVICE_POSTPROCESS (1)
  - SERVICE_SAVE (2)
  - SERVICE_SKETCHFAB (3)
  - SERVICE_PHOTOGRAMMETRY (4)
```

### 3️⃣ **ARCore Wrapper** (C++ - arcore/)

#### ARCoreService
- **지원 모드**:
  - GOOGLE_SFM (Structure from Motion)
  - GOOGLE_TOF (Time of Flight)
  - GOOGLE_FACE (Face Scanning)
  - HUAWEI_SFM (대체 엔진)
  - HUAWEI_TOF
  - HUAWEI_FACE

- **주요 기능**:
  - AR 카메라 초기화 및 관리
  - Frame 처리 (이미지 + Pose)
  - Point Cloud 생성
  - Feature Detection
  - Pose Estimation
  - Depth Map 제공
  - Face Mesh 생성 (Face Mode)

### 4️⃣ **3D 데이터 처리** (C++ - data/)

#### Image
- RGB/Depth 이미지 저장 및 처리
- 이미지 포맷 변환

#### Mesh
- 3D 메시 구조 (정점, 노멀, UV, 색상)
- 메시 연산 (병합, 변환 등)

#### PointCloud
- 특징점 모음
- 깊이 맵 정보

#### Dataset
- 스캔 데이터 저장/로드
- Frame별 Pose 정보 관리
- 카메라 캘리브레이션 저장
- 왜곡(Distortion) 정보 저장

### 5️⃣ **3D 재구성 엔진** (C++ - thread/reconstr.h)

#### Reconstruction Class
```cpp
주요 기능:
- AddPoses(): 프레임별 Pose 데이터 추가
- DetectFeatures(): 이미지 특징점 검출 (OpenCV)
- GetAccuracy(): 특징 매칭 정확도 계산
- Start(): 멀티스레드 기반 재구성 시작
- Undo(): 이전 상태로 복원
- PreviewChange(): Undo 미리보기
```

**재구성 스레드 타입**:
1. **DUMMY**: 테스트용
2. **POSE_CORRECTION**: 포즈 오차 보정
3. **RECONSTRUCTION**: 메인 3D 재구성

### 6️⃣ **3D 렌더링** (C++ - gl/)

#### GLRenderer
- **초기화**: `Init(width, height, rttWidth, rttHeight)`
- **렌더링**: `Render(vertices, normals, uv, colors, indices)`
- **FBO 렌더링**: `Rtt(enable)` - 오프스크린 렌더링 (스크린샷/비디오)
- **카메라**: `GLCamera` 객체로 뷰 변환 제어

### 7️⃣ **3D 편집** (Java - main/Editor.java)

#### Editor Features
- **Select (선택)**:
  - 삼각형 선택
  - 원형 선택 (Circle)
  - 직사각형 선택 (Rect)
  - 전체 선택/해제

- **Colors (색상)**:
  - Contrast 조정
  - Gamma 보정
  - Saturation 조정
  - Tone Mapping

- **Transform (변환)**:
  - Move (이동)
  - Rotate (회전)
  - Scale (스케일)

- **View (뷰)**:
  - First Person
  - Orbit
  - Top Down
  - Floor Plan

### 8️⃣ **후처리 및 내보내기** (C++ - postproc/, exporter/)

#### Texturize
- 메시에 텍스처 맵핑
- Poisson 표면 재구성 (선택사항)

#### Optimizer
- 메시 최적화
- 면 감소 (Simplification)
- 구멍 채우기 (Hole Filling)

#### Exporter
- **출력 포맷**:
  - OBJ + MTL + Texture
  - PLY (Point Cloud)
  - CSV (Pose 정보)
  - Floor Plan (평면도)

---

## 기능 흐름도

### 🔵 **1. 애플리케이션 시작 및 초기화**

```mermaid
sequenceDiagram
    participant User
    participant Home as HomeActivity
    participant Main as Main Activity
    participant JNI as JNI Bridge
    participant Native as ARCoreService

    User->>Home: 앱 실행
    Home->>Home: 권한 확인
    Home->>Home: 설정 로드
    
    User->>Home: 공간/오브젝트 스캔 선택
    Home->>Home: 스캔 모드 저장
    Home->>Main: 스캔 화면 전환
    
    Main->>Main: 레이아웃 초기화
    Main->>Main: GLESSurfaceView 설정
    Main->>Main: 터치 리스너 등록
    
    Main->>JNI: onGlSurfaceCreated()
    JNI->>Native: onARServiceConnected()
    Native->>Native: ARCore 초기화
    Native-->>JNI: 성공/실패
    JNI-->>Main: 콜백 반환
    
    Main->>Main: UI 요소 활성화
```

### 🔴 **2. 실시간 스캔 프로세스**

```mermaid
sequenceDiagram
    participant User
    participant UI as Main Activity
    participant GL as GLSurfaceView
    participant JNI as JNI Bridge
    participant Recon as Reconstruction
    participant Dataset as Dataset
    participant Render as Renderer

    User->>UI: Record 버튼 클릭
    UI->>JNI: onToggleButtonClicked(true)
    JNI->>Recon: Start(RECONSTRUCTION)
    
    loop 실시간 스캔 루프 (각 프레임)
        GL->>JNI: onGlSurfaceDrawFrame()
        
        JNI->>Recon: Process()
        
        par ARCore & Feature Detection
            Recon->>Recon: ARCore에서 카메라 프레임 가져오기
            Recon->>Recon: 특징점 검출 (OpenCV AKAZE)
            Recon->>Recon: Pose 계산
        and 이전 프레임과 매칭
            Recon->>Recon: 특징 매칭
            Recon->>Recon: 정확도 평가
        and 3D 포인트 생성
            Recon->>Recon: 특징점으로부터 3D Point 생성
        end
        
        Recon->>Dataset: Point Cloud 저장
        Recon->>Dataset: Pose 저장
        
        Recon->>Render: Point Cloud 렌더링
        
        Render->>GL: 화면에 렌더
        GL-->>UI: 프레임 표시
    end
    
    User->>UI: Record 버튼 클릭 (일시정지)
    UI->>JNI: onToggleButtonClicked(false)
    JNI->>Recon: Pause
    Recon->>Recon: 재구성 스레드 정지
```

### 🟢 **3. 3D 메시 생성 및 최적화**

```mermaid
sequenceDiagram
    participant Recon as Reconstruction<br/>(Backend)
    participant Tango as Tango3D API
    participant Mesh as Mesh Processing
    participant Scene as Scene

    par Point Cloud → Mesh
        Recon->>Tango: Point Cloud 전달
        Tango->>Tango: Delaunay 삼각분할
        Tango->>Tango: 표면 재구성
        Tango-->>Mesh: Triangle Mesh 반환
    and Mesh 속성 계산
        Mesh->>Mesh: 법선(Normal) 계산
        Mesh->>Mesh: 색상 정보 보간
    end
    
    Mesh->>Scene: 메시 추가
    
    alt 구멍 채우기 활성화
        Mesh->>Mesh: Hole Detection
        Mesh->>Mesh: Hole Filling
    end
    
    Mesh->>Mesh: 불필요한 면 제거
    Mesh->>Mesh: 메시 최적화
```

### 🟠 **4. 편집(Editor) 프로세스**

```mermaid
sequenceDiagram
    participant User
    participant UI as Main Activity
    participant Editor as Editor View
    participant JNI as JNI Bridge
    participant Selector as Selector (C++)
    participant Effect as Effector (C++)

    User->>UI: Editor 버튼 클릭
    UI->>Editor: init() - 편집 모드 시작
    
    alt 선택 기능
        User->>Editor: Select 버튼 선택
        Editor->>Editor: 선택 모드 활성화
        
        User->>Editor: 터치로 영역 선택
        Editor->>JNI: applySelect() or circleSelection()
        JNI->>Selector: 선택 연산 수행
        Selector->>Selector: 메시 재계산
    end
    
    alt 효과 적용
        User->>Editor: 효과 선택 (Contrast/Gamma/등)
        Editor->>JNI: previewEffect()
        JNI->>Effect: 미리보기 적용
        Effect->>Effect: 색상 변환
        
        User->>Editor: 슬라이더 조정
        Editor->>JNI: applyEffect()
        JNI->>Effect: 효과 최종 적용
    end
    
    alt 변환 기능
        User->>Editor: Transform 선택 (Move/Rotate/Scale)
        Editor->>JNI: applyEffect(MOVE/ROTATE/SCALE)
        JNI->>Effect: 변환 연산 수행
    end
    
    User->>UI: Save 버튼
    Editor->>JNI: saveWithTextures()
```

### 🟡 **5. 텍스처 맵핑 및 최종 처리**

```mermaid
sequenceDiagram
    participant UI as Main Activity
    participant Service as BackgroundService
    participant JNI as JNI Bridge
    participant Texturize as Texturize (C++)
    participant Optimizer as Optimizer (C++)
    participant Exporter as Exporter (C++)

    UI->>Service: 저장 요청 (SERVICE_SAVE)
    Service->>JNI: 메인 스레드 시작
    
    JNI->>Texturize: texturize()
    Texturize->>Texturize: 카메라 이미지로 UV 맵핑
    Texturize->>Texturize: 색상 정보 계산
    
    alt Poisson 표면 재구성 활성화
        Texturize->>Texturize: Poisson 알고리즘 실행
        Texturize->>Texturize: 부드러운 표면 생성
    end
    
    Texturize-->>JNI: 텍스처된 메시
    
    alt 최적화 모드
        JNI->>Optimizer: optimize()
        Optimizer->>Optimizer: 불필요한 정점 제거
        Optimizer->>Optimizer: Mesh Simplification
    end
    
    JNI->>Exporter: extract()
    Exporter->>Exporter: 포맷 변환
    Exporter->>Exporter: OBJ + MTL + Texture 생성
    
    Service->>Service: 파일 정렬 및 저장
    Service->>UI: 저장 완료 알림
```

### 🔵 **6. 파일 저장 및 내보내기**

```mermaid
sequenceDiagram
    participant Main as Main Activity
    participant Service as BackgroundService
    participant JNI as JNI Bridge
    participant Exporter as Exporter (C++)
    participant FileSystem as File System
    participant Home as HomeActivity

    Main->>Main: Save 버튼 클릭
    
    alt 실시간 저장 모드
        Main->>Service: SERVICE_SAVE 시작
        Service->>JNI: save(input_path)
        JNI->>Exporter: OBJ 생성
        Exporter->>Exporter: 정점/법선/UV 변환
        Exporter->>FileSystem: model.obj 저장
        Exporter->>FileSystem: model.mtl 저장
        Exporter->>FileSystem: texture.png 저장
        
        Service->>JNI: extract(PLY)
        JNI->>Exporter: Point Cloud 내보내기
        Exporter->>FileSystem: pointcloud.ply 저장
        
    else 나중에 처리 모드
        Main->>Service: SERVICE_SAVE 시작
        Service->>JNI: save()
        JNI->>FileSystem: Dataset 저장
        JNI->>FileSystem: .bin 파일 제거
        
    else Post-Process 모드
        Main->>Service: SERVICE_POSTPROCESS 시작
        Service->>JNI: 텍스처 및 최적화
    end
    
    Service->>Home: 저장 완료
    Home->>Home: FileManager 업데이트
```

### 🟢 **7. 파일 보기 및 공유**

```mermaid
sequenceDiagram
    participant Home as HomeActivity
    participant FM as FileManager
    participant Main as Main Activity (Viewer)
    participant Editor as Editor
    participant Sketchfab as OAuth (Sketchfab)

    Home->>FM: 파일 목록 로드
    FM->>FM: 저장된 OBJ/PLY/Dataset 나열
    
    User->>FM: 파일 선택
    FM->>Main: FILE_KEY 전달
    Main->>JNI: load(file_path)
    JNI->>Main: 메시 로드
    Main->>Main: Viewer Mode 전환
    Main->>Main: 3D 모델 표시
    
    alt 모델 편집
        User->>Main: Editor 버튼
        Main->>Editor: init()
        Editor->>Editor: 편집 모드
    end
    
    alt 공유 기능
        User->>FM: 공유 버튼
        FM->>FM: 파일 압축 (ZIP)
        FM->>Sketchfab: Sketchfab 업로드
        Sketchfab->>Sketchfab: 모델 호스팅
    end
    
    alt 스크린샷/비디오
        User->>Main: 스크린샷
        Main->>JNI: 캡처 요청
        JNI->>Main: PNG 저장
        
        User->>Main: 비디오 녹화
        Main->>Main: 360도 회전 아니메이션 기록
        Main->>Main: MP4 저장
    end
```

---

## 클래스 다이어그램

### Java 클래스 구조

```mermaid
classDiagram
    class Activity {
        <<abstract>>
    }
    
    class AbstractActivity {
        -mCompass: Compass
        +getPath(): File
        +getTempPath(): File
        +deleteRecursive(): void
        +checkPermissions(): void
    }
    
    class HomeActivity {
        -mProgress: ProgressBar
        +onClick(): void
        +startScanning(): void
        +finishScanning(): void
        -checkPermissions(): void
    }
    
    class Main {
        -mGLView: GLESSurfaceView
        -mEditor: Editor
        -mCameraControl: CameraControl
        -m3drRunning: boolean
        -mRecording: boolean
        +onClick(): void
        +onTouch(): boolean
        +onDrawFrame(): void
        -bindAR(): void
        -save(): void
    }
    
    class FileManager {
        -mAdapter: FileAdapter
        -mList: GridView
        +onClick(): void
    }
    
    class JNI {
        <<static>>
        +onARServiceConnected(): boolean
        +onGlSurfaceChanged(): void
        +onGlSurfaceDrawFrame(): boolean
        +onToggleButtonClicked(): void
        +save(): boolean
        +load(): boolean
        +texturize(): void
        +extract(): void
    }
    
    class CameraControl {
        -mView: ViewMode
        -mMoveX, mMoveY, mMoveZ: float
        -mOrbit, mPitch, mYaw: float
        +updateMotion(): void
        +updateCapture(): void
        +setViewerMode(): void
    }
    
    class Editor {
        -mStatus: Status
        -mEffect: Effect
        -mScreen: Screen
        +init(): void
        +touchEvent(): void
        +onClick(): void
        +onDraw(): void
    }
    
    class Service {
        <<static>>
        +process(): void
        +finish(): void
        +reset(): void
        +getRunning(): int
    }
    
    Activity <|-- AbstractActivity
    AbstractActivity <|-- HomeActivity
    AbstractActivity <|-- Main
    AbstractActivity <|-- FileManager
    Main *-- CameraControl
    Main *-- Editor
    Main *-- JNI
    HomeActivity *-- Service
    FileManager *-- Service
```

### C++ 클래스 구조

```mermaid
classDiagram
    class ARCoreService {
        -google: ARCore*
        -renderer: GLRenderer*
        -mode_: Mode
        +Process(): bool
        +GetProjection(): mat4
        +GetView(): mat4
        +GetPointCloud(): vector~vec4~
    }
    
    class ARCore {
        -ar_session_: ArSession*
        -ar_frame_: ArFrame*
        -camera: ARCoreCamera
        -points: vector~vec4~
        +Process(): bool
        +UpdateFeaturePoints(): void
        +GetPointCloud(): vector~vec4~
    }
    
    class Reconstruction {
        -dataset: Dataset*
        -scene: Scene
        -scan: TangoScan
        -threadId: pthread_t
        +Setup(): void
        +Start(): void
        +AddPoses(): void
        +DetectFeatures(): CVDescription
        +Undo(): void
    }
    
    class Dataset {
        -dataset: string
        +ReadPointCloud(): PointCloud
        +ReadPose(): vector~mat4~
        +WritePointCloud(): void
        +WritePose(): void
    }
    
    class Mesh {
        -vertices: vector~vec3~
        -normals: vector~vec3~
        -colors: vector~uint~
        -indices: vector~uint~
        +Transform(): void
        +Merge(): void
    }
    
    class GLRenderer {
        -scene: GLSL*
        -fboID: uint*
        +Init(): void
        +Render(): void
        +Rtt(): void
        +ReadRtt(): Image*
    }
    
    class Exporter {
        <<interface>>
        +Process(): void
        +GetPoseCount(): int
    }
    
    class TangoTexturize {
        +Process(): void
    }
    
    class Optimizer {
        +Process(): void
    }
    
    ARCoreService *-- ARCore
    ARCoreService *-- GLRenderer
    Reconstruction *-- Dataset
    Reconstruction *-- TangoScan
    Reconstruction *-- Scene
    GLRenderer *-- Mesh
    Exporter <|-- TangoTexturize
    Exporter <|-- Optimizer
```

---

## 💡 주요 기술 요소

| 기술 | 용도 | 상태 |
|------|------|------|
| **ARCore** | 모션 추적, 특징 추출 | ✅ |
| **Tango 3D Reconstruction** | 메시 생성 | ✅ |
| **OpenGL ES** | 실시간 렌더링 | ✅ |
| **OpenCV** | 이미지 특징 검출 | ✅ |
| **Poisson** | 표면 재구성 | ✅ |
| **Delaunay** | 삼각분할 | ✅ |
| **GLM** | 수학 연산 | ✅ |

---

## 🔄 데이터 흐름 요약

```
카메라 프레임 → ARCore 추적 → 특징 검출(OpenCV)
    ↓
특징 매칭 → Pose 계산 → 3D Point 생성
    ↓
Point Cloud → Tango3D API → Mesh 생성
    ↓
Mesh 최적화 → Texturize → Export (OBJ/PLY/etc)
    ↓
FileSystem 저장 → FileManager 표시
```

---

## 📊 성능 최적화 포인트

1. **메모리 관리**: 
   - GPU 메모리 할당 (텍스처 개수: 1~8)
   - 해상도 조정 (Default: 0.02f)

2. **렌더링**:
   - FBO를 통한 오프스크린 렌더링
   - 동적 LOD (Level of Detail)

3. **3D 재구성**:
   - 멀티스레드 처리
   - 특징 매칭 최적화
   - Pose 오차 보정

4. **네트워크**:
   - Sketchfab API를 통한 직접 업로드

---

## 🎯 다음 개발 단계

1. ✅ 전체 코드 구조 파악
2. 🔄 기능별 상세 분석 (Current)
3. 📝 새로운 기능 개발 계획 수립
4. 💻 구현 및 테스트

