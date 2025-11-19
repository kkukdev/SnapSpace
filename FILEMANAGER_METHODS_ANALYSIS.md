# FileManager.java의 메서드 분석 및 HomeActivity 이동 가이드

## 📋 FileManager에 있는 관련 메서드들

### **1️⃣ showProgress() - UI 업데이트 메서드**

**위치:** FileManager.java 라인 348-356

```java
public void showProgress() {
    try {
        mAdd.setVisibility(View.GONE);
        mProgress.setVisibility(View.VISIBLE);
    } catch (Exception e) {
        e.printStackTrace();
    }
}
```

**역할:**
- 진행 바(ProgressBar)를 화면에 표시
- "추가" 버튼을 숨김
- 사용자에게 작업이 진행 중임을 알림

**필요성:** ⭐⭐⭐⭐⭐ **필수**
- HomeActivity에 이미 있음 (라인 148)
- 추가로 가져올 필요 없음 ✅

---

### **2️⃣ finishScanning() - 파일 이동 및 정리 메서드** ⭐⭐⭐⭐⭐

**위치:** FileManager.java 라인 439-466

```java
private void finishScanning() {
    mCancel.setVisibility(View.GONE);
    showProgress();
    
    // 타임스탬프로 파일명 생성
    Date date = new Date();
    SimpleDateFormat dateFormat = new SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US);
    final String filename = dateFormat.format(date);
    String text = getString(R.string.data_saved) + " " + filename;
    Toast.makeText(this, text, Toast.LENGTH_LONG).show();

    new Thread(() -> {
        // ★ 핵심 1: 임시 폴더의 파일 경로 가져오기
        File file = new File(Service.getLink(FileManager.this));
        
        // ★ 핵심 2: 파일 이동 (임시 → 최종 폴더)
        File file2save = Exporter.export(file, filename);

        // ★ 핵심 3: 임시 폴더 삭제
        if (!isPostProcessLaterOn(FileManager.this))
            deleteRecursive(new File(file.getParent()));

        // ★ 핵심 4: Service 상태 초기화
        Service.reset(FileManager.this);
        
        // ★ 핵심 5: Main으로 이동 (저장된 파일 표시)
        Intent intent = new Intent(FileManager.this, Main.class);
        intent.putExtra(FILE_KEY, file2save.getAbsolutePath());
        showProgress();
        startActivity(intent);
    }).start();
}
```

**역할:**
- **가장 중요한 메서드!** ← 이것이 파일 이동을 담당
- 백그라운드 스레드에서 실행
- Exporter.export() 호출 → 파일 이동
- 임시 폴더 삭제
- Main으로 이동하며 저장된 파일 경로 전달

**필요성:** ⭐⭐⭐⭐⭐ **필수 (가장 중요!)**
- **반드시 HomeActivity로 이동해야 함** ✅

---

### **3️⃣ startScanning() - 스캔 모드 선택 메서드**

**위치:** FileManager.java 라인 391-438

```java
private void startScanning() {
    // 모드 선택 대화상자 띄우기
    AlertDialog.Builder builder = new AlertDialog.Builder(this);
    builder.setView(R.layout.dialog_scan);
    Dialog dialog = builder.create();
    dialog.getWindow().setBackgroundDrawable(getDrawable(R.drawable.background_dialog));
    dialog.show();

    // Face, Realtime, Dataset 모드 중 선택
    ArrayList<Drawable> icons = new ArrayList<>();
    ArrayList<String> values = new ArrayList<>();
    
    // ... 모드 추가 ...
    
    list.setOnItemClickListener((adapterView, view, index, l) -> {
        dialog.dismiss();
        showProgress();
        
        // SharedPreferences에 모드 저장
        String mode = values.get(index);
        SharedPreferences.Editor e = pref.edit();
        e.putBoolean(getString(R.string.pref_later), true/false);
        e.putString(getString(R.string.pref_mode), "face/realtime/dataset");
        e.commit();
        
        // Main 시작
        startActivity(new Intent(FileManager.this, Main.class));
    });
}
```

**역할:**
- dialog_scan 레이아웃으로 모드 선택 화면 표시
- Face / Realtime / Dataset 중 선택
- SharedPreferences에 선택한 모드 저장
- Main 시작

**필요성:** ❌ **불필요**
- HomeActivity는 이미 직접 "realtime" 모드로 설정
- 모드 선택 대화상자가 필요 없음
- 따라서 가져올 필요 없음 ✅

---

## 🔍 AbstractActivity에 있는 관련 메서드들

### **4️⃣ deleteRecursive() - 디렉토리 삭제 메서드**

**위치:** AbstractActivity.java 라인 87-95

```java
public static void deleteRecursive(File file) {
    for (int i = 0; i < 50; i++) {
        File finalFile = new File(file.getAbsolutePath() + DELETE_POSTFIX + i);
        if (file.renameTo(finalFile)) {
            deleteOnBackground(finalFile);
            return;
        }
    }
    IO.deleteRecursive(file);
}
```

**역할:**
- 디렉토리와 모든 파일을 재귀적으로 삭제
- 안전한 백그라운드 삭제 (deleteOnBackground 사용)
- 임시 폴더(dataset) 삭제

**필요성:** ⭐⭐⭐⭐ **필수**
- **이미 AbstractActivity에 있음** (HomeActivity의 부모 클래스)
- 가져올 필요 없음 (상속으로 사용 가능) ✅
- `deleteRecursive(폴더)` 형태로 호출 가능

---

### **5️⃣ isPostProcessLaterOn() - 후처리 모드 확인 메서드**

**위치:** AbstractActivity.java 라인 148-152

```java
public static boolean isPostProcessLaterOn(Context context) {
    SharedPreferences pref = PreferenceManager.getDefaultSharedPreferences(context);
    boolean later = pref.getBoolean(context.getString(R.string.pref_later), false);
    String mode = pref.getString(context.getString(R.string.pref_mode), "realtime");
    return later || (mode.compareTo("dataset") == 0);
}
```

**역할:**
- SharedPreferences에서 후처리 모드 확인
- pref_later가 true이거나 mode가 "dataset"이면 true 반환
- finishScanning()에서 `if (!isPostProcessLaterOn(...))`로 사용
- 즉, 일반 모드(realtime)면 임시 폴더 삭제

**필요성:** ⭐⭐⭐⭐ **필수**
- **이미 AbstractActivity에 있음** (HomeActivity의 부모 클래스)
- 가져올 필요 없음 (상속으로 사용 가능) ✅
- `isPostProcessLaterOn(this)` 형태로 호출 가능

---

### **6️⃣ getPath() - 최종 저장 폴더 경로 메서드**

**위치:** AbstractActivity.java 라인 298

```java
public static String getPath(boolean migrate) {
    // 최종 저장 폴더 경로 반환
    // AbstractActivity.getPath(false) → 최종 저장 폴더
}
```

**역할:**
- 최종 저장 폴더의 경로를 반환
- Exporter.export()에서 사용됨
- getPath(false) = 최종 저장 폴더

**필요성:** ⭐⭐ **간접 필요**
- **이미 AbstractActivity에 있음**
- Exporter.export()에서 자동으로 사용됨
- 따로 호출할 필요 없음 ✅

---

## 📌 HomeActivity에서 사용할 메서드들

| 메서드 | 위치 | 필요성 | 어디서 가져오나 | 수행 작업 |
|--------|------|--------|-----------------|----------|
| **finishScanning()** | FileManager.java | ⭐⭐⭐⭐⭐ | **복사** | 파일 이동 (핵심!) |
| showProgress() | FileManager.java | ⭐⭐⭐⭐ | HomeActivity에 이미 있음 | UI 표시 |
| deleteRecursive() | AbstractActivity.java | ⭐⭐⭐⭐ | 상속으로 사용 가능 | 임시 폴더 삭제 |
| isPostProcessLaterOn() | AbstractActivity.java | ⭐⭐⭐⭐ | 상속으로 사용 가능 | 모드 확인 |
| getPath() | AbstractActivity.java | ⭐⭐ | 상속으로 사용 가능 (Exporter에서) | 폴더 경로 |
| startScanning() | FileManager.java | ❌ | 불필요 | 모드 선택 (필요 없음) |

---

## 🎯 HomeActivity에서 구현해야 할 것

### **Step 1: onResume() 메서드 추가/수정**

```java
@Override
protected void onResume() {
    super.onResume();
    
    // ★ 저장 완료 상태 확인
    int serviceState = Service.getRunning(this);
    if (serviceState < 0) {
        int absState = Math.abs(serviceState);
        if (absState == Service.SERVICE_SAVE) {
            // finishScanning() 호출!
            finishScanning();
        }
    }
}
```

---

### **Step 2: finishScanning() 메서드 추가**

FileManager.java에서 라인 439-466을 복사해서 HomeActivity에 추가:

```java
private void finishScanning() {
    // mCancel.setVisibility(View.GONE);  // HomeActivity에 mCancel 없으면 제거
    showProgress();
    
    Date date = new Date();
    SimpleDateFormat dateFormat = new SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US);
    final String filename = dateFormat.format(date);
    String text = getString(R.string.data_saved) + " " + filename;
    Toast.makeText(this, text, Toast.LENGTH_LONG).show();

    new Thread(() -> {
        File file = new File(Service.getLink(HomeActivity.this));
        File file2save = Exporter.export(file, filename);

        // 임시 폴더 삭제
        if (!isPostProcessLaterOn(HomeActivity.this))
            deleteRecursive(new File(file.getParent()));

        // 상태 초기화
        Service.reset(HomeActivity.this);
        
        // Main으로 이동 (파일 표시)
        Intent intent = new Intent(HomeActivity.this, Main.class);
        intent.putExtra(FILE_KEY, file2save.getAbsolutePath());
        showProgress();
        startActivity(intent);
    }).start();
}
```

---

## 📝 필요한 Import 추가

```java
import com.snapspace.scanner.main.Exporter;
import java.io.File;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;
```

---

## ✅ 최종 체크리스트

- [ ] HomeActivity.onResume() 메서드 추가/수정
- [ ] Service.getRunning() 상태 확인 로직 추가
- [ ] finishScanning() 메서드 복사 (FileManager.java 라인 439-466)
- [ ] showProgress() 호출 확인 (이미 있음)
- [ ] deleteRecursive() 호출 확인 (상속으로 가능)
- [ ] isPostProcessLaterOn() 호출 확인 (상속으로 가능)
- [ ] FILE_KEY 상수 확인
- [ ] 필요한 Import 추가
- [ ] 컴파일 오류 확인

---

## 🎯 결과

이렇게 하면:

```
1. HomeActivity 진입점
   ↓
2. 스캔 → Main → SAVE
   ↓
3. Service.finish() → SERVICE_RUNNING = -2
   ↓
4. System.exit(0)
   ↓
5. 앱 재시작 → HomeActivity
   ↓
6. HomeActivity.onResume() 호출
   ├─ Service.getRunning() = -2 감지 ✅
   └─ finishScanning() 호출
      ├─ Exporter.export() 실행
      ├─ deleteRecursive() 실행 ← 임시 폴더 삭제
      ├─ Service.reset() 실행
      └─ 파일 저장 완료! ✅
```

모드 3이 완벽하게 동작합니다!
