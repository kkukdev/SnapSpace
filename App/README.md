# App - SnapSpace Mobile Application

## 📱 프로젝트 개요

**App** 폴더는 SnapSpace 프로젝트의 모바일 애플리케이션 부분입니다. 이 부분은 **Android 기반의 3D 스캐닝 및 AR(증강현실) 애플리케이션**을 포함하고 있으며, Google ARCore를 활용한 실시간 환경 인식 및 3D 모델 생성을 지원합니다.

## 🏗️ 주요 구성

### 1. **scanner/** - 메인 Android 애플리케이션

```
scanner/
├── app/                    # Android 애플리케이션 메인 모듈
│   ├── src/               # Java/Kotlin 소스 코드
│   ├── build.gradle       # 의존성 및 빌드 설정
│   └── jni/               # Native 코드 (C/C++)
├── build.gradle           # 프로젝트 레벨 빌드 설정
├── gradle/                # Gradle 래퍼 및 설정
├── gradle.properties      # Gradle 프로퍼티
├── settings.gradle        # Gradle 설정
└── gradlew/gradlew.bat    # Gradle 실행 스크립트
```

**주요 기능:**

- ARCore를 이용한 3D 환경 스캐닝
- 실시간 포인트 클라우드 생성
- 비디오 녹화 및 처리
- REST API를 통한 백엔드 연동 (Retrofit 사용)

**주요 의존성:**

- `com.google.ar:core:1.31.0` - Google ARCore SDK
- `com.squareup.retrofit2` - REST API 클라이언트
- `androidx` - Android 지원 라이브러리
- `jcodec` - 비디오 코덱 처리

### 2. **arcore/** - ARCore 네이티브 라이브러리

```
arcore/
├── include/      # ARCore 헤더 파일
├── jni/          # JNI 바인딩 코드 (Java-C++ 연동)
└── Android.mk    # NDK 빌드 설정
```

ARCore 네이티브 라이브러리와 Java 코드 간의 JNI(Java Native Interface) 바인딩을 담당합니다.

### 3. **common/** - 공유 컴포넌트

```
common/
├── ar/           # AR 관련 공유 클래스
├── arcore/       # ARCore 래퍼 및 유틸리티
├── data/         # 데이터 모델 및 구조
├── editor/       # 3D 편집 관련 모듈
├── exporter/     # 3D 모델 내보내기
├── gl/           # OpenGL 렌더링
├── postproc/     # 후처리 알고리즘
├── record/       # 녹화 및 재생
├── tango/        # Tango API 관련 코드
├── thread/       # 멀티스레딩 유틸리티
└── utils/        # 일반 유틸리티 함수
```

Android 애플리케이션과 다른 모듈에서 공유하는 핵심 기능들을 포함합니다.

### 4. **third_party/** - 외부 라이브러리

```
third_party/
├── delaunay/          # Delaunay 삼각분할
├── glm/               # OpenGL Mathematics 라이브러리
├── libjpeg-turbo/     # 고속 JPEG 인코딩/디코딩
├── libpng/            # PNG 이미지 처리
├── opencv/            # 컴퓨터 비전 라이브러리
├── poisson/           # Poisson 표면 재구성
└── tango_3d_reconstruction/  # Tango 3D 재구성 SDK
```

3D 처리, 이미지 처리, 메시 생성 등에 필요한 오픈소스 라이브러리들입니다.

## 🛠️ 기술 스택

- **플랫폼:** Android 7+ (Min SDK: 24)
- **언어:** Java, C/C++
- **프레임워크:** Google ARCore, AndroidX
- **빌드 시스템:** Gradle
- **3D 처리:** OpenGL, Poisson, Delaunay
- **이미지 처리:** OpenCV, libjpeg-turbo, libpng
- **네트워킹:** Retrofit2
- **보안:** OpenID AppAuth

## 📋 빌드 및 실행

### 빌드 요구사항

- Android Studio 2023.1.x 이상
- Android NDK r25.2.9519653 이상
- Java 11 이상
- Gradle 8.0 이상

### 빌드 방법

#### Windows

```bash
cd scanner
gradlew.bat assembleRelease
```

#### macOS/Linux

```bash
cd scanner
./gradlew assembleRelease
```

### APK 생성

```bash
# Debug APK
gradlew.bat assembleDebug

# Release APK
gradlew.bat assembleRelease
```

## 🔧 개발 가이드

### 프로젝트 구조 이해

1. **scanner/app/src/main** - 메인 Android 애플리케이션 코드
2. **common/** - 재사용 가능한 모듈 (scanner/app의 build.gradle에서 참조)
3. **arcore/** - 네이티브 AR 기능 구현

### 주요 클래스/모듈

- **AR 처리:** `common/ar/`, `common/arcore/`
- **3D 렌더링:** `common/gl/`
- **3D 메시 생성:** `common/exporter/`, `third_party/poisson/`
- **비디오 처리:** `common/record/`
- **네트워킹:** `scanner/app/src/main` (Retrofit 사용)

## 📦 배포

### APK 서명

Release APK는 자동으로 프로젝트 서명 키로 서명됩니다. 프로덕션 배포 시 별도의 서명 키를 설정하세요.

### 혼동 방지 (Proguard)

Release 빌드 시 ProGuard를 통해 자동으로 코드 난독화가 적용됩니다.

- 설정 파일: `scanner/proguard-rules.pro`

## 📝 라이선스

본 프로젝트는 여러 오픈소스 라이브러리를 사용합니다:

- **GLM:** MIT 라이선스
- **libpng:** libpng 라이선스
- **OpenCV, ARCore 등:** 각 라이브러리의 라이선스 참조

자세한 내용은 `NOTICE.txt` 파일을 참조하세요.

## 🔗 관련 링크

- **메인 프로젝트:** SnapSpace (SSAFY 13기 자율프로젝트)
- **백엔드:** `BackEnd/` 폴더 참조
- **웹 프론트엔드:** `working/admin/my-app/` 참조
- **AI 처리:** `AI/` 폴더 참조

## ❓ FAQ

**Q: 컴파일 에러가 발생합니다.**

- NDK 설정 확인: Android Studio의 Settings > SDK Manager에서 NDK 설치 확인
- Gradle 캐시 초기화: `gradlew clean`

**Q: ARCore 기능이 작동하지 않습니다.**

- 디바이스가 ARCore 지원 기기인지 확인
- ARCore 앱이 설치되어 있는지 확인
- 카메라 권한이 허용되었는지 확인

## 👥 팀 정보

SSAFY 13기 자율프로젝트 S102팀