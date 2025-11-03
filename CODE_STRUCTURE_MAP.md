# 3D Live Scanner - 코드 구조 맵 & 핵심 클래스

## 📌 코드 위치 가이드

### Java 클래스 (Android UI Layer)

```
App/scanner/app/src/main/java/com/snapspace/scanner/

📁 main/                    ← 핵심 스캔 로직
├─ Main.java              ← 메인 스캔 UI (가장 중요)
│  ├─ onCreate()          : UI 초기화
│  ├─ bindAR()            : AR 세션 초기화
│  ├─ onGlSurfaceDrawFrame() : 매 프레임 렌더링
│  ├─ onClick()           : 버튼 이벤트 처리
│  ├─ onTouch()           : 터치 이벤트 처리
│  ├─ save()              : 파일 저장
│  └─ setViewerMode()     : Viewer 모드 설정
│
├─ JNI.java               ← Native 함수 선언
│  ├─ onARServiceConnected() 📍
│  ├─ onGlSurfaceDrawFrame()
│  ├─ onToggleButtonClicked()
│  ├─ onClearButtonClicked()
│  ├─ onUndoButtonClicked()
│  ├─ save()
│  ├─ load()
│  └─ texturize()
│
├─ CameraControl.java      ← 카메라 뷰 제어
│  ├─ ViewMode (FACE, ORBIT, TOPDOWN, FIRST, FLOORPLAN)
│  ├─ updateMotion()      : 제스처 처리
│  ├─ updateCapture()     : 렌더링 업데이트
│  ├─ setViewerMode()     : Viewer 모드
│  └─ captureBitmap()     : 스크린샷
│
├─ Editor.java            ← 3D 편집 도구
│  ├─ Status (IDLE, SELECT_OBJECT, SELECT_CIRCLE, ...)
│  ├─ Effect (CONTRAST, GAMMA, SATURATION, TONE, CLONE, DELETE, ...)
│  ├─ init()              : 에디터 초기화
│  ├─ onClick()           : 버튼 클릭
│  ├─ onDraw()            : 커스텀 드로잉
│  └─ touchEvent()        : 터치 이벤트
│
├─ Exporter.java          ← 파일 포맷 변환
│  ├─ export()            : OBJ/PLY 내보내기
│  ├─ compressModel()     : ZIP 압축
│  └─ getObjResources()   : 리소스 파일 목록
│
├─ DistanceMeasuring.java  ← 거리 측정 도구
├─ Indicators.java         ← 상태 표시기
└─ HandMotionView.java     ← 손 움직임 표시

📁 ui/                     ← UI 및 파일 관리
├─ HomeActivity.java      ← 시작 화면
│  ├─ onClick()
│  ├─ startScanning()     : 스캔 시작
│  ├─ finishScanning()    : 스캔 완료
│  └─ checkPermissions()  : 권한 확인
│
├─ AbstractActivity.java   ← 기본 Activity
│  ├─ FILE_KEY            : Intent Key
│  ├─ TEMP_DIRECTORY      : 임시 디렉토리
│  ├─ getPath()           : 저장 경로
│  ├─ getTempPath()       : 임시 경로
│  ├─ deleteRecursive()   : 재귀 삭제
│  ├─ convertDpToPx()     : DIP 변환
│  └─ checkPermissions()  : 권한 확인
│
├─ FileManager.java       ← 파일 관리자
│  ├─ mAdapter (FileAdapter)
│  ├─ mList (GridView)
│  ├─ onClick()
│  ├─ onItemClick()       : 파일 선택
│  ├─ onOptions()         : 옵션 메뉴
│  └─ loadFiles()         : 파일 로드
│
├─ Service.java           ← 백그라운드 서비스
│  ├─ SERVICE_POSTPROCESS
│  ├─ SERVICE_SAVE
│  ├─ SERVICE_SKETCHFAB
│  ├─ onCreate()          : 서비스 시작
│  ├─ process()           : 비동기 작업 시작
│  ├─ finish()            : 작업 완료
│  └─ reset()             : 상태 초기화
│
├─ Settings.java          ← 설정 화면
├─ Uploader.java          ← Sketchfab 업로드
├─ Initializator.java     ← 초기화 로직
├─ CommonDialogs.java     ← 다이얼로그 유틸
├─ RenameDialog.java      ← 이름 변경 다이얼로그
└─ FileAdapter.java       ← 파일 목록 어댑터

📁 sketchfab/            ← Sketchfab 연동
└─ OAuth.java             ← 3D 모델 업로드
```

### C/C++ 코드 (Native Layer)

```
App/common/

📁 arcore/               ← AR 엔진 래퍼
├─ arcore.h/cc          ← Google ARCore
│  ├─ ARCore class
│  ├─ ar_session_, ar_frame_
│  ├─ Process()
│  ├─ GetPointCloud()
│  ├─ GetProjection(), GetView()
│  └─ HitTest()
│
├─ arengine.h/cc        ← Huawei AREngine
├─ service.h/cc         ← ARCoreService (래퍼)
│  ├─ Mode (GOOGLE_SFM, GOOGLE_TOF, GOOGLE_FACE, ...)
│  ├─ Process()
│  ├─ GetPointCloud()
│  ├─ GetPose()
│  └─ RenderCamera()
│
├─ camera.h/cc          ← 카메라 관리
│  ├─ ARCoreCamera
│  ├─ Effect (GRAYSCALE, NONE, ...)
│  ├─ NightVisionScheme
│  └─ RenderCamera()
│
└─ platform.h           ← 플랫폼 추상화

📁 data/                 ← 데이터 구조
├─ image.h/cc           ← RGB/Depth 이미지
├─ mesh.h/cc            ← 3D 메시 구조
│  ├─ vertices, normals, uv
│  ├─ colors, indices
│  ├─ Transform()
│  └─ Merge()
│
├─ file3d.h/cc          ← 3D 파일 I/O
├─ depthmap.h/cc        ← 깊이 맵
├─ dataset.h/cc         ← 스캔 데이터 저장소 📍
│  ├─ GetPath()
│  ├─ ReadPointCloud()  : 프레임 N의 Point Cloud
│  ├─ ReadPose()        : 프레임 N의 카메라 Pose
│  ├─ ReadPreview()     : 메시 미리보기
│  ├─ WritePointCloud()
│  ├─ WritePose()
│  └─ WriteState()
│
└─ mesh.h/cc

📁 thread/              ← 멀티스레드 처리 (핵심!) 📍
├─ reconstr.h/cc        ← Reconstruction 클래스
│  ├─ Dataset* dataset  : 데이터 저장소
│  ├─ Scene scene
│  ├─ TangoScan scan
│  ├─ TangoTexturize texturize
│  ├─ Selector selector : 편집 선택 도구
│  ├─ GLRenderer* renderer : 렌더러
│  │
│  ├─ Setup()           : 초기화
│  ├─ Start()           : 멀티스레드 시작
│  ├─ AddPoses()        : Pose 추가
│  ├─ DetectFeatures()  : 특징점 검출
│  ├─ Process()         : 메인 루프
│  ├─ PreviewChange()   : Undo 미리보기
│  ├─ Undo()            : 되돌리기
│  └─ RenderGL()        : 렌더링
│
└─ scene.h/cc           ← 3D 장면 관리

📁 gl/                  ← OpenGL 렌더링
├─ renderer.h/cc        ← GLRenderer (핵심!) 📍
│  ├─ Init()            : 초기화
│  ├─ Render()          : 메시 렌더링
│  ├─ Rtt()             : 오프스크린 렌더링
│  ├─ ReadRtt()         : 프레임버퍼 읽기
│  └─ camera (GLCamera)
│
├─ camera.h/cc          ← GL 카메라
├─ scene.h/cc           ← GL 장면
├─ glsl.h/cc            ← Shader 관리
└─ opengl.h             ← GL 상수

📁 exporter/            ← 파일 내보내기
├─ exporter.h/cc        ← Exporter (기본 클래스)
│  ├─ GetPoseCount()
│  ├─ Process()         : 추상 함수
│  └─ ConvertFrame()
│
├─ ply.h/cc             ← PLY 형식
│  └─ PLY 점 구름 내보내기
│
├─ floorpln.h/cc        ← 평면도 추출
├─ csvposes.h/cc        ← CSV Pose 저장
└─ depthmaps.h/cc       ← 깊이맵 내보내기

📁 postproc/            ← 후처리 알고리즘
├─ texturize.h/cc       ← 텍스처 맵핑
│  ├─ Process()         : 메시 텍스처 적용
│  ├─ 카메라 이미지 사용
│  └─ 색상 정보 계산
│
├─ optimizer.h/cc       ← 메시 최적화
│  ├─ 면 감소 (Simplification)
│  ├─ 불필요 정점 제거
│  └─ 구멍 채우기
│
└─ poisson.h/cc         ← Poisson 표면 재구성

📁 editor/              ← 3D 편집 도구 (C++)
├─ selector.h/cc        ← 삼각형/영역 선택
│  ├─ applySelect()
│  ├─ circleSelection()
│  ├─ rectSelection()
│  └─ completeSelection()
│
├─ effector.h/cc        ← 효과 적용
│  ├─ applyEffect()     : Contrast, Gamma, ...
│  ├─ previewEffect()
│  └─ Transform (Move, Rotate, Scale)
│
└─ rasterizer.h/cc      ← 래스터화

📁 tango/               ← Tango 3D Reconstruction
├─ retango.h/cc         ← Retango 관리
│  ├─ Tango3D 래퍼
│  └─ 메시 재구성
│
├─ scan.h/cc            ← TangoScan
│  ├─ Point Cloud 관리
│  └─ Pose 연산
│
└─ texturize.h/cc       ← Tango 텍스처

📁 utils/               ← 유틸리티
├─ com/                 ← Java 브릿지
│  └─ lvonasek/utils/
│     ├─ Compass.java   : 나침반 / IMU
│     ├─ GPS.java       : GPS 위치 기록
│     ├─ IO.java        : 파일 I/O
│     ├─ Compatibility.java : 기기 호환성
│     └─ GestureDetector.java : 터치 제스처
│
└─ C++ 유틸리티

📁 ar/com/              ← Java AR 유틸리티
└─ lvonasek/
   └─ utils/
      ├─ Compass.java
      ├─ GPS.java
      ├─ IO.java
      ├─ Compatibility.java
      ├─ GestureDetector.java
      └─ GLESSurfaceView.java ← OpenGL 렌더링 뷰
```

---

## 🔑 핵심 데이터 구조

### Frame/Pose 저장 구조

```cpp
// Dataset 클래스 (C++)
class Dataset {
    // 파일 저장 구조
    dataset_path/
    ├─ frame_0001.depth     ← Point Cloud 데이터
    ├─ frame_0001.pose      ← 카메라 위치 & 회전
    ├─ frame_0002.depth
    ├─ frame_0002.pose
    ├─ ...
    ├─ state.txt            ← 카메라 캘리브레이션
    ├─ distortion.txt       ← 렌즈 왜곡 정보
    └─ metadata.json        ← 스캔 정보
    
    // 메서드
    ReadPointCloud(int index)   // index 프레임의 포인트 클라우드
    ReadPose(int index)         // index 프레임의 Pose 행렬
    WritePointCloud(int, data)  // 저장
    WritePose(int, data)
}

// Pose 행렬 구조 (4x4 변환 행렬)
┌                              ┐
│ R00 R01 R02 | Tx            │
│ R10 R11 R12 | Ty            │  ← 카메라 → 월드 좌표
│ R20 R21 R22 | Tz            │
│   0   0   0 |  1            │
└                              ┘
R: 회전 행렬 (3x3)
T: 변환 벡터 (3x1)
```

### Mesh 데이터 구조

```cpp
class Mesh {
    std::vector<glm::vec3> vertices;  // 정점 좌표
    std::vector<glm::vec3> normals;   // 법선 벡터
    std::vector<glm::vec2> uv;        // UV 텍스처 좌표
    std::vector<uint32_t> colors;     // RGBA 색상
    std::vector<uint32_t> indices;    // 면 인덱스
    
    // 예시
    vertices[0] = {0.0f, 0.0f, 0.0f}  // 정점 1
    vertices[1] = {1.0f, 0.0f, 0.0f}  // 정점 2
    vertices[2] = {0.0f, 1.0f, 0.0f}  // 정점 3
    
    indices[0] = 0  // 삼각형 1: 정점 0-1-2
    indices[1] = 1
    indices[2] = 2
    
    indices[3] = 1  // 삼각형 2: 정점 1-3-2
    indices[4] = 3
    indices[5] = 2
}
```

### Point Cloud 포맷

```cpp
Tango3DR_PointCloud {
    // 특징점 데이터
    points[]    // xyz 좌표
    normals[]   // 법선 벡터
    colors[]    // rgb 색상
    timestamp
    
    // 특성
    최대 포인트 수: 제한 없음
    정확도: ARCore 의존
    업데이트: 매 프레임
}
```

---

## 📡 JNI 호출 순서

### 초기화 단계

```
Java Main.bindAR()
  └─> JNI.onARServiceConnected()
      └─> Native: ARCoreService::ARCoreService()
          └─> ARCore 초기화
          └─> Reconstruction 준비
          └─> return true
      └─> JNI.onToggleButtonClicked(false)
          └─> Reconstruction 대기
      └─> Java Main.mInitialised = true
```

### 렌더링 루프

```
GL Thread: GLSurfaceView.onDrawFrame()
  └─> JNI.onGlSurfaceDrawFrame()
      └─> Native: onGlSurfaceDrawFrame()
          ├─> ARCore.Process()      // 카메라 프레임 & Pose
          ├─> Reconstruction 데이터 확인
          ├─> GLRenderer.Render()   // 메시 렌더링
          └─> return true (if hand motion)
      └─> GL 화면 표시
```

### 저장 단계

```
Java Main.save()
  └─> Service.process(SERVICE_SAVE)
      └─> JNI.save(path)
          └─> Native: save()
              ├─> Mesh → OBJ 변환
              ├─> 파일 쓰기
              └─> return true
      └─> JNI.extract(PLY)
          └─> Native: extract()
              ├─> Point Cloud → PLY 변환
              └─> 파일 쓰기
      └─> Service.finish()
          └─> 앱 종료
```

---

## 🔗 주요 클래스 연결 지도

```
┌────────────────────────────────────────┐
│     Android UI Thread                  │
│  ┌─────────────────────────────────┐  │
│  │ HomeActivity                    │  │
│  └──────────┬──────────────────────┘  │
│             │ startActivity()         │
│  ┌──────────▼──────────────────────┐  │
│  │ Main Activity                   │  │
│  │ ├─ GLESSurfaceView              │  │
│  │ ├─ CameraControl                │  │
│  │ ├─ Editor                       │  │
│  │ └─ JNI 호출                      │  │
│  └──────────┬──────────────────────┘  │
│             │ onCreate()              │
│  ┌──────────▼──────────────────────┐  │
│  │ FileManager / Settings          │  │
│  └─────────────────────────────────┘  │
└────────────────────────────────────────┘
         │ JNI bridge
    ┌────▼─────────────────────────────┐
    │  Native C++ Code (libc++_shared) │
    │                                  │
    │  ┌──────────────────────────┐   │
    │  │ ARCoreService            │   │
    │  │ ├─ ARCore                │   │
    │  │ └─ GLRenderer            │   │
    │  └──────────┬───────────────┘   │
    │             │ Start()            │
    │  ┌──────────▼───────────────┐   │
    │  │ Reconstruction           │   │
    │  │ (Background Thread)      │   │
    │  │ ├─ DetectFeatures()      │   │
    │  │ ├─ Tango3D API          │   │
    │  │ ├─ Dataset              │   │
    │  │ └─ Mesh Generation      │   │
    │  └──────────┬───────────────┘   │
    │             │ texturize()        │
    │  ┌──────────▼───────────────┐   │
    │  │ Texturize / Optimizer    │   │
    │  │ └─ Export (OBJ/PLY)      │   │
    │  └──────────────────────────┘   │
    │                                  │
    └────────────────────────────────────┘
         │ 파일 저장
    ┌────▼─────────────────────────────┐
    │  File System                     │
    │  ├─ /3D Live Scanner/            │
    │  │  ├─ model.obj                 │
    │  │  ├─ model.mtl                 │
    │  │  ├─ texture.png               │
    │  │  ├─ pointcloud.ply            │
    │  │  └─ ...                       │
    │  └─ /dataset (temp)              │
    │     ├─ frame_*.depth             │
    │     ├─ frame_*.pose              │
    │     └─ ...                       │
    └────────────────────────────────────┘
```

---

## 🎯 파일 접근 가이드

### UI 로직이 필요한 경우
```
Main.java
├─ onClick() / onTouch()    ← 유저 입력 처리
├─ bindAR()                 ← AR 초기화
├─ onDrawFrame()            ← 렌더링 루프
└─ save()                   ← 저장 로직
```

### 3D 데이터 처리가 필요한 경우
```
C++/thread/reconstr.h
├─ Setup()                  ← 초기화
├─ Start()                  ← 멀티스레드 시작
├─ Process()                ← 메인 루프
└─ Undo()                   ← 되돌리기
```

### 파일 I/O가 필요한 경우
```
Java: FileManager.java / Exporter.java
C++: data/dataset.h / exporter/*.h
```

### AR/카메라 제어가 필요한 경우
```
Java: CameraControl.java
C++: arcore/service.h
```

### 렌더링 커스터마이징이 필요한 경우
```
C++: gl/renderer.h
Java: CameraControl.captureBitmap()
```

---

## 📚 추천 학습 순서

1. **Java UI 이해**
   - Main.java 전체
   - HomeActivity.java
   - CameraControl.java

2. **ARCore 기초**
   - arcore/service.h
   - JNI.java (메서드 선언)

3. **3D 처리 파이프라인**
   - thread/reconstr.h
   - data/dataset.h
   - data/mesh.h

4. **렌더링**
   - gl/renderer.h
   - CameraControl.captureBitmap()

5. **편집 도구**
   - editor/effector.h
   - editor/selector.h
   - Editor.java

6. **내보내기**
   - exporter/*.h
   - Exporter.java

