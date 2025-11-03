# 기존 앱 흐름 분석 & activity_home.xml 개편 계획

## 목차
1. [현재 앱 구조 분석](#현재-앱-구조-분석)
2. [당신의 이해가 맞는지 검증](#당신의-이해가-맞는지-검증)
3. [새로운 구조의 가능성](#새로운-구조의-가능성)
4. [구현 과정 상세 가이드](#구현-과정-상세-가이드)

---

## 현재 앱 구조 분석

### 1. 앱 실행 흐름 (현재)

```
┌─────────────────────────────────────────────────────────────┐
│                    앱 최초 실행                               │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
        ┌────────────────────────────┐
        │  Initializator.java        │
        │  (엔트리 포인트)             │
        └────────────────┬───────────┘
                         │
                         ▼
        ┌────────────────────────────────────┐
        │  FileManager.java + 관련 UI        │
        │  (파일 목록/관리 화면)             │
        │  - 스캔된 파일 목록 표시          │
        │  - "ADD" 버튼 표시               │
        └────────────────┬────────────────────┘
                         │
                         ▼ (사용자가 "ADD" 클릭)
        ┌──────────────────────────────────┐
        │  startScanning()                 │
        │  (FileManager 내부 메서드)       │
        │  → dialog_scan.xml 모달 띄움      │
        └────────────────┬─────────────────┘
                         │
        ┌────────────────┴────────────────┐
        │   모달에서 모드 선택              │
        └────────────────┬─────────────────┘
                         │
        ┌────────────────┴────────────────────────────┐
        │                                              │
        ▼                                              ▼
    [FACE 모드]                              [REALTIME 모드]
        │                                              │
        ├─→ pref.putString(                          ├─→ pref.putString(
        │   "pref_mode", "face")                      │   "pref_mode", "realtime")
        │                                              │
        └─────────────────┬──────────────────────────┘
                          │
                    (선택사항 저장)
                          │
                          ▼
        ┌─────────────────────────────────┐
        │  startActivity(Main.class)      │
        │  (Main.java로 전환)             │
        └─────────────────────────────────┘
                          │
                          ▼
        ┌─────────────────────────────────┐
        │  Main.java의 onCreate()         │
        │  → SharedPreferences에서 모드   │
        │    읽어서 스캔 시작             │
        └─────────────────────────────────┘
```

### 2. 현재 FileManager의 핵심 코드

```java
// FileManager.java에서 "ADD" 버튼 클릭 처리
@Override
public void onClick(View v) {
    int id = v.getId();
    
    if (id == R.id.add_button) {
        // GPS 권한 확인 (선택사항)
        SharedPreferences pref = PreferenceManager.getDefaultSharedPreferences(this);
        if (pref.getBoolean(getString(R.string.pref_gps), false)) {
            String[] permissions = {
                    Manifest.permission.ACCESS_COARSE_LOCATION,
                    Manifest.permission.ACCESS_FINE_LOCATION
            };
            onPermissionSuccess = this::startScanning;
            askForPermissions(permissions);
        } else {
            startScanning();  // ← 이 함수가 모달을 띄움
        }
    }
}

// 모달을 띄우는 함수
private void startScanning() {
    AlertDialog.Builder builder = new AlertDialog.Builder(this);
    builder.setView(R.layout.dialog_scan);  // ← 모드 선택 모달
    Dialog dialog = builder.create();
    dialog.getWindow().setBackgroundDrawable(getDrawable(R.drawable.background_dialog));
    dialog.show();

    // 모드 버튼들 생성 (FACE, REALTIME, DATASET)
    ArrayList<Drawable> icons = new ArrayList<>();
    ArrayList<String> values = new ArrayList<>();
    if (Compatibility.isARSupported(this)) {
        icons.add(getDrawable(R.drawable.ic_type_face));
        values.add(getString(R.string.mode_face));
        icons.add(getDrawable(R.drawable.ic_type_scan));
        values.add(getString(R.string.mode_realtime));
        if (isProVersion(this)) {
            icons.add(getDrawable(R.drawable.ic_type_dataset));
            values.add(getString(R.string.mode_dataset));
        }
    }

    ArrayAdapterWithIcons adapter = new ArrayAdapterWithIcons(this, values, icons);
    GridView list = dialog.findViewById(R.id.list);
    list.setAdapter(adapter);
    list.setOnItemClickListener((adapterView, view, index, l) -> {
        dialog.dismiss();
        showProgress();

        String mode = values.get(index);
        SharedPreferences.Editor e = pref.edit();
        
        // ← 선택한 모드를 SharedPreferences에 저장
        if (mode.compareTo(getString(R.string.mode_dataset)) == 0) {
            e.putBoolean(getString(R.string.pref_later), true);
            e.putString(getString(R.string.pref_mode), "realtime");
        } else if (mode.compareTo(getString(R.string.mode_face)) == 0) {
            e.putBoolean(getString(R.string.pref_later), false);
            e.putString(getString(R.string.pref_mode), "face");
        } else if (mode.compareTo(getString(R.string.mode_realtime)) == 0) {
            e.putBoolean(getString(R.string.pref_later), false);
            e.putString(getString(R.string.pref_mode), "realtime");
        }
        e.commit();

        // ← Main.java로 Intent 발송
        startActivity(new Intent(FileManager.this, Main.class));
    });
}
```

### 3. Main.java에서 모드 읽기

```java
// Main.java의 onCreate()에서
String pref_mode = pref.getString(getString(R.string.pref_mode), "realtime");

if (pref_mode.compareTo("face") == 0) {
    // 얼굴 스캔 모드
    mCameraControl.updateView(CameraControl.ViewMode.FACE);
} else if (pref_mode.compareTo("realtime") == 0) {
    // 일반 3D 스캔 모드
    // ...
}
```

---

## 당신의 이해가 맞는지 검증

### ✅ 맞는 부분

1. **앱 실행 시작 구조**
   - ✅ Initializator → FileManager (현재)
   - ✅ Initializator → activity_home.xml (예정)

2. **모달 선택 과정**
   - ✅ FileManager에서 "ADD" 클릭 → dialog_scan 모달 띄움
   - ✅ 모달에서 모드(INDOOR/OUTDOOR 또는 FACE/REALTIME) 선택
   - ✅ SharedPreferences에 선택값 저장
   - ✅ Main.java로 Intent 발송

3. **Main.java에서 처리**
   - ✅ SharedPreferences에서 모드 읽음
   - ✅ 해당 모드로 스캔 시작

### ❓ 명확히 해야 할 부분

**"공간 스캔 버튼을 누르면 바로 스캔이 시작되었으면 좋겠어"** 에서:

```
질문 1: "바로 스캔이 시작"이라는 게 정확히 뭘 의미하나요?
  
  옵션 A: 공간 스캔 버튼 클릭 → 모달 띄움 (INDOOR/OUTDOOR 선택) 
                          → 선택하면 Main.java로 이동
                          
  옵션 B: 공간 스캔 버튼 클릭 → 바로 Main.java의 스캔 화면으로 이동
                          (모달 없음, INDOOR는 기본값으로)
                          
  옵션 C: 공간 스캔 버튼 클릭 → activity_home.xml에서 직접 Intent 보내기
                          (FileManager 거치지 않음)
```

---

## 새로운 구조의 가능성

### 현재 구조
```
Initializator (엔트리) 
     ↓
FileManager (파일 관리)
     ↓
Main.java (스캔)
```

### 새로운 구조
```
Initializator (엔트리)
     ↓
activity_home.xml (홈 화면 - 새로 추가)
     ├─ "공간 스캔" 버튼
     ├─ "오브젝트 스캔" 버튼
     ├─ "스캔 미리보기" 버튼
     └─ "서버 업로드" 버튼
     ↓
Main.java (스캔)
```

---

## 구현 과정 상세 가이드

### 📋 Step 1: 구조 파악 (당신이 이미 이해함) ✓

**현재 플로우:**
```
FileManager.startScanning()
  └─ dialog_scan 모달 띄우기
     └─ ArrayAdapterWithIcons로 모드 옵션 표시
        └─ 선택 → SharedPreferences 저장
           └─ Main.java로 Intent 발송
```

**새로운 플로우:**
```
activity_home.xml의 공간 스캔 버튼 클릭
  └─ 어떤 Activity에서 처리?
     ├─ 옵션 1: activity_home 전용 Activity 만들기
     ├─ 옵션 2: FileManager를 activity_home으로 변경
     └─ 옵션 3: Initializator에서 직접 처리
```

---

### 📋 Step 2: 핵심 결정 - 3가지 구현 방식

#### **방식 1: activity_home 전용 Activity 만들기** (추천 ⭐)

```
장점:
  ✅ 깔끔한 구조
  ✅ 기존 Main.java 건드리지 않음
  ✅ activity_home.xml이 독립적으로 작동
  ✅ 나중에 확장하기 쉬움
  
단점:
  ❌ 새 Activity 클래스 필요
  ❌ 약간 더 복잡함

구조:
  Initializator
    ↓
  HomeActivity (새로 만들기)
    ├─ setContentView(R.layout.activity_home)
    ├─ "공간 스캔" 클릭 처리
    │   └─ startScanning() (FileManager에서 복사)
    │      └─ dialog_scan 모달 띄우기
    │
    └─ Main.java로 Intent 발송
```

#### **방식 2: FileManager를 activity_home로 변경** (중간)

```
장점:
  ✅ 새 클래스 안 만들어도 됨
  ✅ 기존 코드 활용 가능
  ✅ 적응 시간 짧음
  
단점:
  ❌ FileManager 로직이 혼잡해짐
  ❌ 파일 관리와 홈 화면이 섞임
  ❌ 나중에 분리하기 어려움

구조:
  Initializator
    ↓
  FileManager (수정됨)
    ├─ activity_home.xml 로드
    ├─ "공간 스캔" 버튼에 startScanning() 연결
    └─ 나머지 기존 로직
```

#### **방식 3: Initializator에서 직접 처리** (단순)

```
장점:
  ✅ 가장 간단함
  ✅ 코드 최소 수정
  
단점:
  ❌ Initializator가 비대해짐
  ❌ 모달 처리가 Initializator에 들어감
  ❌ 나중에 유지보수 어려움
  
구조:
  Initializator (크기 증가)
    ├─ activity_home.xml 로드
    ├─ 버튼 클릭 처리
    ├─ dialog_scan 모달 띄우기
    └─ Main.java로 Intent 발송
```

---

### 📋 Step 3: 각 방식별 구현 단계

#### **[추천] 방식 1 상세 구현 단계**

```
Step 3-1: HomeActivity 클래스 생성
  ├─ extends AbstractActivity
  ├─ implements View.OnClickListener
  ├─ onCreate()에서 activity_home.xml 로드
  ├─ 공간 스캔 버튼에 onClick 리스너 등록
  └─ 다른 버튼들도 추가 (미리보기, 업로드 등)

Step 3-2: startScanning() 메서드 추가
  ├─ FileManager.java의 startScanning() 코드 복사
  ├─ dialog_scan 모달 띄우기
  ├─ 모드 선택 리스너 구현
  ├─ SharedPreferences에 모드 저장
  └─ Main.java로 Intent 발송

Step 3-3: Initializator 수정
  ├─ Initializator.onResume()에서
  ├─ FileManager 대신 HomeActivity 시작
  └─ Intent를 HomeActivity로 변경

Step 3-4: AndroidManifest.xml 수정
  ├─ HomeActivity 등록
  └─ 엔트리 포인트 확인
```

**Step 3-2 상세: startScanning() 복사 시 주의사항**

```java
// FileManager에서 이 부분을 복사:
private void startScanning() {
    AlertDialog.Builder builder = new AlertDialog.Builder(this);
    builder.setView(R.layout.dialog_scan);
    Dialog dialog = builder.create();
    dialog.getWindow().setBackgroundDrawable(
        getDrawable(R.drawable.background_dialog));
    dialog.show();

    ArrayList<Drawable> icons = new ArrayList<>();
    ArrayList<String> values = new ArrayList<>();
    
    // ↓ 이 부분이 핵심: 모드를 선택하면...
    if (Compatibility.isARSupported(this)) {
        icons.add(getDrawable(R.drawable.ic_type_face));
        values.add(getString(R.string.mode_face));
        icons.add(getDrawable(R.drawable.ic_type_scan));
        values.add(getString(R.string.mode_realtime));
        if (isProVersion(this)) {
            icons.add(getDrawable(R.drawable.ic_type_dataset));
            values.add(getString(R.string.mode_dataset));
        }
    }

    SharedPreferences pref = 
        PreferenceManager.getDefaultSharedPreferences(HomeActivity.this);
    ArrayAdapterWithIcons adapter = 
        new ArrayAdapterWithIcons(this, values, icons);
    GridView list = dialog.findViewById(R.id.list);
    list.setAdapter(adapter);
    list.setOnTouchListener((v, event) -> 
        event.getAction() == MotionEvent.ACTION_MOVE);
    
    // ↓ 이 부분: 모드 선택 처리
    list.setOnItemClickListener((adapterView, view, index, l) -> {
        dialog.dismiss();
        showProgress();

        String mode = values.get(index);
        SharedPreferences.Editor e = pref.edit();
        
        if (mode.compareTo(getString(R.string.mode_dataset)) == 0) {
            e.putBoolean(getString(R.string.pref_later), true);
            e.putString(getString(R.string.pref_mode), "realtime");
        } else if (mode.compareTo(getString(R.string.mode_face)) == 0) {
            e.putBoolean(getString(R.string.pref_later), false);
            e.putString(getString(R.string.pref_mode), "face");
        } else if (mode.compareTo(getString(R.string.mode_realtime)) == 0) {
            e.putBoolean(getString(R.string.pref_later), false);
            e.putString(getString(R.string.pref_mode), "realtime");
        }
        e.commit();

        // ↓ 마지막: Main.java로 이동
        startActivity(new Intent(HomeActivity.this, Main.class));
    });
}
```

---

### 📋 Step 4: 버튼별 처리 방식

#### **activity_home.xml의 버튼들**

```xml
<ImageView id="@+id/space_scan_button" />           <!-- 공간 스캔 -->
<ImageView id="@+id/object_scan_button" />          <!-- 오브젝트 스캔 -->
<Button id="@+id/preview_button" />                 <!-- 스캔 미리보기 -->
<Button id="@+id/upload_button" />                  <!-- 서버 업로드 -->
```

#### **각 버튼의 처리 로직**

```java
@Override
public void onClick(View v) {
    int id = v.getId();
    
    if (id == R.id.space_scan_button) {
        // "공간 스캔" → startScanning() 호출
        // → dialog_scan 모달 띄우기
        // → 모드 선택 → Main.java로 이동
        startScanning();
        
    } else if (id == R.id.object_scan_button) {
        // "오브젝트 스캔" → FileManager로 이동
        // (또는 별도 로직)
        startActivity(new Intent(this, FileManager.class));
        
    } else if (id == R.id.preview_button) {
        // "스캔 미리보기" → FileManager로 이동
        // (저장된 스캔 파일 목록 보기)
        startActivity(new Intent(this, FileManager.class));
        
    } else if (id == R.id.upload_button) {
        // "서버 업로드" → Uploader로 이동
        startActivity(new Intent(this, Uploader.class));
    }
}
```

---

### 📋 Step 5: 데이터 흐름 정리

#### **SharedPreferences에 저장되는 값들**

```java
// 모드 저장
pref.putString("pref_mode", "realtime");  // or "face", or "dataset"

// 데이터셋 나중 저장 여부
pref.putBoolean("pref_later", false);     // true = 데이터셋 모드
```

#### **Main.java에서 읽는 방식** (변경 없음)

```java
String pref_mode = pref.getString(getString(R.string.pref_mode), "realtime");
boolean pref_later = pref.getBoolean(getString(R.string.pref_later), false);

if (pref_mode.compareTo("face") == 0) {
    // 얼굴 스캔 모드
    mCameraControl.updateView(CameraControl.ViewMode.FACE);
} else {
    // 일반 3D 스캔 모드
}
```

---

## 최종 정리

### ✅ 당신의 이해가 맞습니다

**현재 흐름:**
```
Initializator 
  → FileManager (ADD 버튼)
    → dialog_scan 모달 (모드 선택)
      → SharedPreferences 저장
        → Main.java
```

### ✅ 새로운 흐름 (제안)

```
Initializator 
  → HomeActivity (새로 만들기)
    → "공간 스캔" 버튼
      → dialog_scan 모달 (모드 선택)
        → SharedPreferences 저장
          → Main.java
```

### ✅ 구현 가능성

**100% 가능합니다!** ✓

이유:
- 기존 코드는 그대로 사용
- 단지 UI 레이어만 변경
- Main.java는 건드리지 않음
- dialog_scan 모달 로직만 복사

### ✅ 필요한 작업 (요약)

```
1. HomeActivity 클래스 만들기
   └─ startScanning() 메서드 추가 (FileManager에서 복사)

2. Initializator 수정
   └─ FileManager 대신 HomeActivity 시작

3. AndroidManifest.xml 수정
   └─ HomeActivity 등록

4. activity_home.xml의 버튼 ID와
   HomeActivity.onClick()를 연결

(Main.java는 건드리지 않음!)
```

### 💡 추가 고려사항

**"공간 스캔"과 "오브젝트 스캔"의 차이:**

```
현재 코드에서는 dialog_scan에서 다음 모드들을 제공:
  - FACE (얼굴 스캔)
  - REALTIME (일반 3D 스캔)
  - DATASET (데이터셋 모드 - 프로 버전만)

"공간 스캔"과 "오브젝트 스캔"이 정확히 뭔지 결정해야:
  
  옵션 1: 
    - "공간 스캔" = REALTIME 모드
    - "오브젝트 스캔" = FACE 모드
    
  옵션 2:
    - "공간 스캔" = 해상도 높음
    - "오브젝트 스캔" = 해상도 낮음
    (같은 모드이지만 설정만 다름)
    
  옵션 3:
    - "공간 스캔" = dialog_scan 모달 띄우기
    - "오브젝트 스캔" = FileManager로 이동
```

---

## 결론

당신의 이해가 정확하며, **"이렇게 만드는 게 가능한가?"에 대한 답은 명확하게 YES입니다.**

필요한 모든 코드는 이미 FileManager.java에 있으며, 단지 그것을 새로운 HomeActivity로 이동시키고 Initializator를 수정하면 됩니다. Main.java는 건드릴 필요가 없습니다.

다음 단계: 위 3가지 방식 중 하나를 선택하고, 실제 코드 구현을 시작하면 됩니다.
