# 3D Live Scanner - 프로젝트 요약 & 개발 체크리스트

## 📊 프로젝트 개요

| 항목 | 내용 |
|------|------|
| **프로젝트명** | 3D Live Scanner (SSAFY 13기 자율프로젝트 S102팀) |
| **목적** | Android 기반 실시간 AR 3D 스캔 및 편집 앱 |
| **주요 언어** | Java (Android UI), C++ (Core 3D) |
| **AR 엔진** | Google ARCore (주), Huawei AREngine (대체) |
| **3D 처리** | Tango 3D Reconstruction API |
| **렌더링** | OpenGL ES |
| **플랫폼** | Android 7.0+ |

---

## 🎯 핵심 기능

### ✅ 구현된 기능

| 기능 | 상태 | 위치 |
|------|------|------|
| **실시간 3D 스캔** | ✅ | Main.java / reconstr.h |
| **카메라 트래킹** | ✅ | arcore/service.h |
| **Point Cloud 생성** | ✅ | tango/scan.h |
| **메시 생성 및 최적화** | ✅ | gl/renderer.h |
| **텍스처 맵핑** | ✅ | postproc/texturize.h |
| **3D 편집 (Select/Color/Transform)** | ✅ | editor/, Editor.java |
| **Undo/Redo** | ✅ | thread/reconstr.h |
| **파일 저장 (OBJ/PLY)** | ✅ | exporter/, Exporter.java |
| **파일 관리** | ✅ | FileManager.java |
| **Sketchfab 업로드** | ✅ | OAuth.java |
| **GPS 좌표 기록** | ✅ | GPS.java |
| **Photo Mode** | ✅ | Main.java |

### 🔄 개발 예정 기능

| 기능 | 상태 | 우선순위 |
|------|------|---------|
| **Object Scan Mode** | 🔧 | High |
| **Face Recognition** | 🔧 | Medium |
| **Advanced AI Editing** | 📋 | Medium |
| **Cloud Sync** | 📋 | Low |
| **Multiplayer Scanning** | 📋 | Low |

---

## 🏗️ 아키텍처 계층

```
┌─────────────────────────────────┐
│   User Interface Layer          │
│  (HomeActivity, Main, Editor)   │
├─────────────────────────────────┤
│   Android Service Layer         │
│  (BackgroundService, JNI)       │
├─────────────────────────────────┤
│   AR Processing Layer           │
│  (ARCoreService, Reconstruction)│
├─────────────────────────────────┤
│   3D Data Processing            │
│  (Dataset, Mesh, Point Cloud)   │
├─────────────────────────────────┤
│   Graphics & Export             │
│  (GLRenderer, Exporter)         │
├─────────────────────────────────┤
│   External Libraries            │
│  (OpenCV, Poisson, GLM)         │
└─────────────────────────────────┘
```

---

## 📁 디렉토리 구조 (간단 버전)

```
App/
├── arcore/              # ARCore 바이너리
├── common/
│   ├── arcore/         # AR 엔진 래퍼
│   ├── data/           # 데이터 구조
│   ├── editor/         # 편집 도구
│   ├── exporter/       # 내보내기
│   ├── gl/             # 렌더링
│   ├── postproc/       # 후처리
│   ├── thread/         # 멀티스레드 (Reconstruction)
│   ├── tango/          # Tango 3D API
│   └── utils/          # 유틸리티
├── scanner/            # Android Project
│   └── app/src/main/java/com/snapspace/scanner/
│       ├── main/       # 스캔 UI
│       ├── ui/         # 파일 관리 UI
│       └── sketchfab/  # 업로드
└── third_party/        # 외부 라이브러리
```

---

## 🔀 데이터 흐름

### 스캔 시작
```
HomeActivity (시작) 
  ↓
Main.java (스캔 UI) 
  ↓ JNI
ARCoreService 초기화
  ↓
Reconstruction 스레드 시작
  ↓ (매 프레임)
ARCore.Process() → Feature Detection → 3D Point → Mesh 생성
```

### 저장
```
Main.save()
  ↓
Service.process(SERVICE_SAVE)
  ↓ JNI
JNI.save() → OBJ 생성
  ↓
JNI.extract() → PLY 생성
  ↓
JNI.texturize() → 텍스처 맵핑
  ↓
FileManager에 표시
```

---

## 🛠️ 주요 기술 스택

| 계층 | 기술 | 용도 |
|------|------|------|
| **UI** | Android Framework | UI 구성 |
| **AR** | Google ARCore | 카메라 추적, 포즈 추정 |
| **3D** | Tango 3D API | 메시 생성 (Delaunay) |
| **Vision** | OpenCV | 특징 검출 (AKAZE) |
| **Graphics** | OpenGL ES | 실시간 렌더링 |
| **Surface** | Poisson | 표면 재구성 |
| **Math** | GLM | 벡터/행렬 연산 |

---

## 💻 개발 환경 설정

### 필수 환경
- Android Studio 4.0+
- NDK r21+
- CMake 3.10+
- Android SDK 24+

### 빌드
```bash
cd App/scanner
./gradlew assembleRelease
```

### 실행
```bash
adb install app/build/outputs/apk/release/scanner-release.apk
```

---

## 📚 주요 클래스 & 메서드

### Java 핵심 메서드

| 클래스 | 메서드 | 용도 |
|--------|--------|------|
| **Main** | `bindAR()` | AR 초기화 |
| | `onDrawFrame()` | 렌더링 루프 |
| | `onClick()` | 버튼 처리 |
| | `save()` | 파일 저장 |
| **CameraControl** | `updateMotion()` | 제스처 처리 |
| **Editor** | `touchEvent()` | 편집 터치 |
| **FileManager** | `loadFiles()` | 파일 로드 |

### C++ 핵심 메서드

| 클래스 | 메서드 | 용도 |
|--------|--------|------|
| **ARCoreService** | `Process()` | 매 프레임 처리 |
| **Reconstruction** | `Start()` | 재구성 스레드 시작 |
| | `AddPoses()` | Pose 추가 |
| | `Undo()` | 되돌리기 |
| **GLRenderer** | `Render()` | 메시 렌더링 |
| **Dataset** | `WritePose()` | Pose 저장 |
| | `ReadPose()` | Pose 로드 |

---

## 🔍 코드 이해 우선순위

### Tier 1 (필수)
1. `Main.java` - 앱의 중심
2. `thread/reconstr.h` - 3D 처리 엔진
3. `data/dataset.h` - 데이터 저장소

### Tier 2 (중요)
4. `arcore/service.h` - AR 엔진
5. `gl/renderer.h` - 렌더링
6. `CameraControl.java` - 카메라 제어

### Tier 3 (참고)
7. `Editor.java` - 편집 도구
8. `Exporter.java` - 내보내기
9. 각 유틸리티 클래스

---

## 🚀 개발 가이드

### 새로운 기능 추가

#### 1단계: 요구사항 분석
- [ ] 어느 계층에 추가할지 결정 (UI/Native)
- [ ] 기존 코드와의 상호작용 파악
- [ ] 성능 영향 예측

#### 2단계: 설계
- [ ] 필요한 새 클래스/메서드 정의
- [ ] 데이터 구조 설계
- [ ] JNI 연결 필요 여부 확인

#### 3단계: 구현
```
Java 파일:
- App/scanner/app/src/main/java/...

C++ 파일:
- App/common/.../header.h
- App/common/.../source.cc
```

#### 4단계: 테스트
```bash
# 빌드
./gradlew clean build

# 설치
adb install -r app/build/outputs/apk/debug/scanner-debug.apk

# 로그 확인
adb logcat arcore_app:V
```

#### 5단계: 통합
- [ ] 코드 리뷰
- [ ] 테스트 케이스 추가
- [ ] 문서 업데이트

---

## 🐛 일반적인 문제 해결

### Build Error

**Error**: NDK 경로 없음
```bash
# local.properties 확인
cat App/scanner/local.properties
# 필요시 추가
ndk.dir=/path/to/ndk
sdk.dir=/path/to/sdk
```

**Error**: CMake 버전
```bash
# Android Studio에서 설정
SDK Manager → NDK Side by side → 최신 버전 설치
```

### Runtime Error

**Error**: "Unable to load library '3dscanner'"
- NDK 바이너리 누락
- ABI 불일치 (arm64-v8a vs armeabi-v7a)
- 해결: `gradlew clean build`

**Error**: ARCore 초기화 실패
- ARCore 미설치
- 기기 지원 안 함
- 해결: Google Play Services 업데이트

### Permission Error

```java
// AndroidManifest.xml 확인
<uses-permission android:name="android.permission.CAMERA" />
<uses-permission android:name="android.permission.WRITE_EXTERNAL_STORAGE" />
```

---

## 📋 개발 체크리스트

### 프로젝트 이해 단계
- [ ] PROJECT_ARCHITECTURE_ANALYSIS.md 읽기
- [ ] FEATURE_FLOW_DETAILS.md 읽기
- [ ] CODE_STRUCTURE_MAP.md 읽기
- [ ] 주요 클래스 코드 검토

### 개발 환경 설정
- [ ] Android Studio 설치
- [ ] NDK r21+ 설치
- [ ] CMake 설치
- [ ] 프로젝트 Sync
- [ ] 빌드 성공

### 첫 번째 기능 개발
- [ ] 요구사항 명확히
- [ ] 기존 코드 분석
- [ ] 설계 문서 작성
- [ ] 코드 구현
- [ ] 테스트 수행
- [ ] 코드 리뷰

### 배포 준비
- [ ] 성능 최적화
- [ ] 메모리 누수 확인
- [ ] 크래시 테스트
- [ ] 사용자 문서 작성

---

## 📞 참고 문서

### 내부 문서
- `PROJECT_ARCHITECTURE_ANALYSIS.md` - 전체 아키텍처
- `FEATURE_FLOW_DETAILS.md` - 기능별 상세 흐름
- `CODE_STRUCTURE_MAP.md` - 코드 구조 맵
- `CURRENT_STRUCTURE_ANALYSIS.md` - 현재 구조 분석
- `FILEMANAGER_METHODS_ANALYSIS.md` - FileManager 분석

### 외부 문서
- [Google ARCore Docs](https://developers.google.com/ar/develop)
- [Android NDK Docs](https://developer.android.com/ndk/guides)
- [OpenCV Tutorials](https://docs.opencv.org/)
- [Tango 3D API](https://github.com/googlearchive/tango-examples-c)
- [Poisson Surface Reconstruction](http://www.cs.jhu.edu/~misha/Code/PoissonRecon/)

---

## 🎓 학습 경로

### 주간 1: 기초 이해
- Day 1-2: Java UI 분석 (Main.java, HomeActivity.java)
- Day 3-4: ARCore 및 3D 기초
- Day 5: 데이터 흐름 전체 이해

### 주간 2-3: 핵심 시스템
- Day 1-3: Reconstruction 엔진 깊이있게
- Day 4-5: 렌더링 파이프라인

### 주간 4: 개발 시작
- 새 기능 설계 및 구현

---

## 🎯 다음 단계

1. **현재 상태**: ✅ 전체 코드 구조 파악 완료
2. **다음**: 기능별 상세 분석 (Current in CODE_STRUCTURE_MAP.md)
3. **그 다음**: 새로운 기능 개발 계획 수립
4. **최종**: 구현 및 테스트

---

## 📝 문서 버전 정보

| 문서 | 버전 | 작성일 | 내용 |
|------|------|--------|------|
| PROJECT_ARCHITECTURE_ANALYSIS.md | 1.0 | 2024-11-03 | 전체 아키텍처 분석 |
| FEATURE_FLOW_DETAILS.md | 1.0 | 2024-11-03 | 기능별 상세 흐름도 |
| CODE_STRUCTURE_MAP.md | 1.0 | 2024-11-03 | 코드 구조 및 클래스 지도 |
| README_PROJECT_SUMMARY.md | 1.0 | 2024-11-03 | 프로젝트 요약 |

---

## 💡 팁과 노하우

### 효율적인 코드 탐색
```bash
# 특정 함수 찾기
grep -r "functionName" App/common/

# 클래스 정의 찾기
grep -r "class ClassName" App/

# JNI 연결 확인
grep -r "onARServiceConnected" .
```

### 디버깅
```bash
# 로그 필터링
adb logcat arcore_app:V *:E

# Crash 분석
adb logcat | grep -E "CRASH|Exception"

# Native 디버깅
Android Studio → Debug → 중단점 설정
```

### 성능 모니터링
- Android Profiler로 CPU/Memory/GPU 확인
- Frame Rate 모니터링
- 메모리 누수 확인

---

**문서 작성 완료!** 🎉

다음 단계: 
1. 이 문서들을 참고하여 코드 개발 시작
2. 기능별로 FEATURE_FLOW_DETAILS.md의 시퀀스 다이어그램 참고
3. 새로운 기능 추가 시 CODE_STRUCTURE_MAP.md에서 적절한 위치 파악
4. 질문이나 추가 분석 필요시 언제든지 요청

