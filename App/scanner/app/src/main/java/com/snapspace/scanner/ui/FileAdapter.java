package com.snapspace.scanner.ui;

import android.app.AlertDialog;
import android.content.Context;
import android.content.Intent;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.drawable.BitmapDrawable;
import android.graphics.drawable.Drawable;
import android.net.Uri;
import android.text.SpannableString;
import android.util.Log; // <-- [추가] 로그 import
import android.view.LayoutInflater;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.widget.BaseAdapter;
import android.widget.ImageView;
import android.widget.TextView;
import android.widget.Toast; // <-- [추가] Toast import
import android.widget.EditText;

import androidx.core.content.FileProvider;
import com.snapspace.scanner.BuildConfig;


import com.snapspace.scanner.R;
import com.snapspace.scanner.main.Exporter;
import com.snapspace.scanner.main.Main;
import com.snapspace.scanner.utils.FastApiService; // <-- 'fastapi'가 'network'로 변경됨 (이전 코드 기준)
import com.lvonasek.utils.GestureDetector;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashMap;
import java.util.Locale;
import java.util.Scanner;

import android.widget.RadioGroup;
import android.widget.RadioButton;
import android.widget.LinearLayout;

// --- [추가] Retrofit 및 OkHttp import ---
import okhttp3.MediaType;
import okhttp3.MultipartBody;
import okhttp3.RequestBody;
import retrofit2.Call;
import retrofit2.Callback;
import retrofit2.Response;
// --- import 끝 ---

class FileAdapter extends BaseAdapter
{
  private FileManager mContext;
  private String mPath;
  private ArrayList<Integer> mSelected;
  private GestureDetector mGesture;
  private float mColumns;

  private final HashMap<String, Drawable> mIcons = new HashMap<>();
  private final ArrayList<String> mItems = new ArrayList<>();

  FileAdapter(FileManager context, int columns)
  {
    mContext = context;
    mColumns = columns;
    mPath = AbstractActivity.getPath(false);
    mSelected = new ArrayList<>();

    mContext.setColumns(columns);
    mGesture = new GestureDetector(new GestureDetector.GestureListener() {
      @Override
      public boolean IsAcceptingRotation() {
        return false;
      }

      @Override
      public void OnDrag(float dx, float dy) {
      }

      @Override
      public void OnTwoFingerMove(float dx, float dy) {
      }

      @Override
      public void OnTwoFingerRotation(float angle) {
      }

      @Override
      public void OnPinchToZoom(float diff) {
//        int before = (int)mColumns;
//        mColumns -= diff * 0.75f;
//        if (mColumns < 2) {
//          mColumns = 2;
//        }
//        if (mColumns > 5) {
//          mColumns = 5;
//        }
//        int after = (int)mColumns;
//
//        if (before != after) {
//          mContext.setColumns(after);
//        }
      }
    }, mContext);
  }

  @Override
  public int getCount()
  {
    return mItems.size();
  }

  @Override
  public Object getItem(int i)
  {
    return mItems.get(i);
  }

  @Override
  public long getItemId(int i)
  {
    return i;
  }

  @Override
  public View getView(final int index, View view, ViewGroup viewGroup)
  {
    LayoutInflater inflater = (LayoutInflater) mContext.getSystemService(Context.LAYOUT_INFLATER_SERVICE);
    view = inflater.inflate(R.layout.view_item, null, true);
    if (getCount() <= index) {
      return view;
    }
    String key = (String)getItem(index);
    TextView name = view.findViewById(R.id.name);
    if (key.lastIndexOf('.') > 0) {
      name.setText(key.substring(0, key.lastIndexOf('.')));
    } else {
      name.setText(key);
    }

    //set icon
    boolean hasExtension = false;
    ImageView icon = view.findViewById(R.id.icon);
    icon.setImageDrawable(mContext.getDrawable(R.drawable.ic_folder));
    if (key.compareTo(mContext.getString(R.string.folder_up)) == 0) {
      icon.setImageDrawable(mContext.getDrawable(R.drawable.ic_folder_up));
    }
    for (String ext : Exporter.FILE_EXT) {
      if (key.endsWith(ext)) {
        icon.setImageDrawable(mContext.getDrawable(R.drawable.ic_model_icon));
        hasExtension = true;
        break;
      }
    }
    synchronized (mIcons) {
      if (mIcons.containsKey(key)) {
        icon.setImageDrawable(mIcons.get(key));
      }
    }
    loadIcon(key, icon);

    //set selection
    View selection = view.findViewById(R.id.selection);
    if (mSelected.contains(index)) {
      selection.setVisibility(View.VISIBLE);
    }

    //set open action
    boolean finalHasExtension = hasExtension;
    view.setOnClickListener(v -> {
      if (!mSelected.isEmpty()) {
        setSelected(selection, index);
        return;
      }

      if (!finalHasExtension) {
        if (hasParent() && (index == 0)) {
          mPath = new File(mPath).getParentFile().getAbsolutePath();
        } else {
          mPath = new File(mPath, key).getAbsolutePath();
        }
        mContext.refreshUI();
      } else {
        File f = new File(getPath(), key);
        Intent intent = new Intent(mContext, Main.class);
        intent.putExtra(AbstractActivity.FILE_KEY, f.getAbsolutePath());
        mContext.showProgress();
        mContext.startActivity(intent);
      }
    });

    view.setOnLongClickListener(view1 -> {
      setSelected(selection, index);
      return true;
    });

    return view;
  }

  public boolean hasParent() {
    return (getPath() + "/").compareTo(AbstractActivity.getPath(false)) != 0;
  }

  public void toParent() {
    mPath = new File(mPath).getParentFile().getAbsolutePath();
    mContext.refreshUI();
  }

  private void loadIcon(String name, ImageView view) {
    new Thread(() -> {
      try {
        File thumbFile = null;
        if (name.endsWith(Exporter.EXT_DATASET)) {
          thumbFile = new File(getPath(), name + "/thumbnail.jpg");
          if (!thumbFile.exists()) {
            File file = new File(getPath(), name + "/00000000.jpg");
            if (file.exists()) {
              Bitmap originalBitmap = BitmapFactory.decodeFile(file.getAbsolutePath());;
              Bitmap resizedBitmap = Bitmap.createScaledBitmap(originalBitmap, 180, 320, false);
              try (FileOutputStream out = new FileOutputStream(thumbFile.getAbsolutePath())) {
                resizedBitmap.compress(Bitmap.CompressFormat.JPEG, 75, out);
              } catch (Exception e) {
                e.printStackTrace();
              }
            }
          }
        } else if (name.endsWith(Exporter.EXT_OBJ)) {
          thumbFile = new File(getPath(), name + "/thumbnail.jpg");
          if (!thumbFile.exists()) {
            File f = new File(getPath(), name);
            if (f.exists()) {
              File model = AbstractActivity.getModel(f);
              String mtl = Exporter.getMtlResource(model.getAbsolutePath());
              File file = new File(model.getParent(), mtl + ".png");
              if (file.exists()) {
                Bitmap originalBitmap = BitmapFactory.decodeFile(file.getAbsolutePath());;
                Bitmap resizedBitmap = Bitmap.createScaledBitmap(originalBitmap, 256, 256, false);
                try (FileOutputStream out = new FileOutputStream(thumbFile.getAbsolutePath())) {
                  resizedBitmap.compress(Bitmap.CompressFormat.JPEG, 75, out);
                } catch (Exception e) {
                  e.printStackTrace();
                }
              }
            }
          }
        }
        if (thumbFile != null && thumbFile.exists() && thumbFile.length() >= 1024) {
          Bitmap bitmap = BitmapFactory.decodeFile(thumbFile.getAbsolutePath());
          int w = bitmap.getWidth();
          int h = bitmap.getHeight();
          int o = (Math.max(w, h) - Math.min(w, h)) / 2;
          if (w < h) {
            bitmap = Bitmap.createBitmap(bitmap, 0, o, w, w);
          } else if (w > h) {
            bitmap = Bitmap.createBitmap(bitmap, o, 0, h, h);
          }
          if ((bitmap.getWidth() != 256) || (bitmap.getHeight() != 256)) {
            bitmap = Bitmap.createScaledBitmap(bitmap, 256, 256, true);
          }
          Drawable d = new BitmapDrawable(mContext.getResources(), bitmap);
          synchronized (mIcons) {
            if (mIcons.containsKey(name)) {
              mIcons.remove(name);
            }
            mIcons.put(name, d);
          }
          mContext.runOnUiThread(() -> view.setImageDrawable(d));
        }
      } catch (Exception e) {
        e.printStackTrace();
      }
    }).start();
  }

  public String getPath() {
    return new File(mPath).getAbsolutePath();
  }

  public void update() {
    mItems.clear();
    mSelected.clear();
    mContext.setOptions(mSelected.size());

    if (hasParent()) {
      mItems.add(mContext.getString(R.string.folder_up));
    }

    String[] files = new File(getPath()).list();
    if (files != null) {
      Arrays.sort(files);

      ArrayList<String> folders = new ArrayList<>();
      for (String s : files) {
        if (s.contains(AbstractActivity.DELETE_POSTFIX)) {
          continue;
        }
        File f = new File(getPath(), s);
        if (f.isDirectory()) {
          if (f.getAbsolutePath().compareTo(mContext.getTempPath().getAbsolutePath()) != 0) {
            if (Exporter.isFolder(s)) {
              folders.add(s);
            }
          }
        }
      }

      ArrayList<String> data = new ArrayList<>();
      for (String s : files) {
        if (s.contains(AbstractActivity.DELETE_POSTFIX)) {
          continue;
        }
        if (Exporter.isFolder(s)) {
          continue;
        }
        File f = new File(getPath(), s);
        if (f.isDirectory()) {
          if (f.getAbsolutePath().compareTo(mContext.getTempPath().getAbsolutePath()) != 0) {
            if (s.startsWith("20")) {
              data.add(0, s);
            } else {
              data.add(s);
            }
          }
        }
      }

      mItems.addAll(folders);
      mItems.addAll(data);
    }
    notifyDataSetChanged();
  }

  public boolean hasPosition() {
    if (mSelected.isEmpty()) {
      return false;
    }
    String key = (String)getItem(mSelected.get(0));
    File gpsFile = new File(new File(getPath(), key), "position.txt");
    return gpsFile.exists();
  }

  public void showPosition() {
    String key = (String)getItem(mSelected.get(0));
    File gpsFile = new File(new File(getPath(), key), "position.txt");
    try {
      Scanner sc = new Scanner(new FileInputStream(gpsFile.getAbsolutePath()));
      sc.useLocale(Locale.US);
      String lon = sc.next();
      String lat = sc.next();
      sc.close();

      Uri uri = Uri.parse("geo:" + lat + "," + lon + "");
      Intent intent = new Intent(android.content.Intent.ACTION_VIEW, uri);
      mContext.startActivity(intent);
    } catch (Exception e) {
      e.printStackTrace();
    }
  }

  public void deleteModel() {
    AlertDialog.Builder deleteDlg = new AlertDialog.Builder(mContext);
    deleteDlg.setTitle(mContext.getString(R.string.delete));
    deleteDlg.setMessage(mContext.getString(R.string.continue_question));
    deleteDlg.setPositiveButton(mContext.getString(android.R.string.ok), (dialogInterface, i) -> {
      for (Integer index : mSelected) {
        String key = (String)getItem(index);
        AbstractActivity.deleteRecursive(new File(getPath(), key));
      }
      mContext.refreshUI();
    });
    deleteDlg.setNegativeButton(mContext.getString(android.R.string.cancel), null);

    AlertDialog d = deleteDlg.create();
    d.getWindow().setBackgroundDrawable(mContext.getDrawable(R.drawable.background_dialog));
    d.show();
  }

  public void shareModel() {
    String key = (String)getItem(mSelected.get(0));
    if (key.length() <= 4) {
      Toast.makeText(mContext, R.string.invalid_name, Toast.LENGTH_LONG).show();
    } else {
      AlertDialog.Builder dialog = new AlertDialog.Builder(mContext);
      
      // 타이틀을 bold로 설정
      SpannableString title = new SpannableString(mContext.getString(R.string.share_via));
      title.setSpan(new android.text.style.StyleSpan(android.graphics.Typeface.BOLD), 0, title.length(), 0);
      dialog.setTitle(title);
      
      dialog.setItems(R.array.shares, (dialog1, which) -> {
        mContext.showProgress();
        new Thread(() -> {
          File file = new File(getPath(), key);
          final String zip = Exporter.compressModel(file);
          mContext.runOnUiThread(() -> {
            Intent intent;
            switch (which) {
              case 0:
                intent = new Intent(Intent.ACTION_SEND);
                intent.setType("application/zip");
                intent.putExtra(Intent.EXTRA_STREAM, FileProvider.getUriForFile(mContext, BuildConfig.APPLICATION_ID + ".provider", new File(zip)));
                mContext.startActivity(Intent.createChooser(intent, mContext.getString(R.string.share_via)));
                break;
              case 1: // [수정] FastAPI 업로드
                promptForGroupName(zip);
                //uploadToFastApi(zip);
                break;
            }
            mContext.refreshUI();
          });
        }).start();
      });
      AlertDialog d = dialog.create();
      d.getWindow().setBackgroundDrawable(mContext.getDrawable(R.drawable.background_dialog));
      d.show();
    }
  }

  public void rename() {
    String key = (String)getItem(mSelected.get(0));
    new RenameDialog(mContext, getPath(), key);
  }

  public void setSelected(View selection, int index) {
    if (hasParent() && (index == 0)) {
      return;
    }
    if (mSelected.contains(index)) {
      mSelected.remove((Integer) index);
      selection.setVisibility(View.GONE);
    } else {
      mSelected.add(index);
      selection.setVisibility(View.VISIBLE);
    }

    mContext.setOptions(mSelected.size());
  }

  public String getSelected() {
    if (mSelected.isEmpty()) {
      return null;
    } else if (mSelected.size() == 1) {
      return (String)getItem(mSelected.get(0));
    } else {
      return mContext.getString(R.string.more_items) + " (" + mSelected.size() + ")";
    }
  }

  public boolean hasExtension() {
    if (mSelected.isEmpty()) {
      return false;
    }

    String key = (String)getItem(mSelected.get(0));
    for (String ext : Exporter.FILE_EXT) {
      if (key.endsWith(ext)) {
        return true;
      }
    }
    return false;
  }

  public void forwardTouch(MotionEvent event) {
    mGesture.onTouchEvent(event);
  }

  private void promptForGroupName(String zipFilePath) {
    AlertDialog.Builder builder = new AlertDialog.Builder(mContext);
    builder.setTitle("업로드 정보 입력");

    // 커스텀 레이아웃 생성
    LinearLayout container = new LinearLayout(mContext);
    container.setOrientation(LinearLayout.VERTICAL);

    float density = mContext.getResources().getDisplayMetrics().density;
    int marginPx = (int)(20 * density);
    container.setPadding(marginPx, marginPx/2, marginPx, marginPx/2);

    // 1. 그룹 이름 입력
    TextView groupLabel = new TextView(mContext);
    groupLabel.setText("그룹 이름");
    groupLabel.setTextSize(14);
    groupLabel.setPadding(0, 0, 0, (int)(8 * density));
    container.addView(groupLabel);

    final EditText input = new EditText(mContext);
    input.setHint("예: A공장 B구역");
    input.setSingleLine(true);
    container.addView(input);

    // 2. 모델 타입 선택
    TextView typeLabel = new TextView(mContext);
    typeLabel.setText("모델 타입");
    typeLabel.setTextSize(14);
    typeLabel.setPadding(0, (int)(16 * density), 0, (int)(8 * density));
    container.addView(typeLabel);

    final RadioGroup radioGroup = new RadioGroup(mContext);
    radioGroup.setOrientation(RadioGroup.VERTICAL);

    RadioButton radioSpace = new RadioButton(mContext);
    radioSpace.setText("공간 스캔 (space)");
    radioSpace.setId(View.generateViewId());
    radioGroup.addView(radioSpace);

    RadioButton radioObject = new RadioButton(mContext);
    radioObject.setText("오브젝트 스캔 (object)");
    radioObject.setId(View.generateViewId());
    radioObject.setChecked(true); // 기본값: object
    radioGroup.addView(radioObject);

    container.addView(radioGroup);

    builder.setView(container);

    // 확인 버튼
    builder.setPositiveButton(mContext.getString(android.R.string.ok), (dialog, which) -> {
      String groupName = input.getText().toString().trim();

      if (groupName.isEmpty()) {
        Toast.makeText(mContext, "그룹 이름을 입력해야 합니다.", Toast.LENGTH_SHORT).show();
        mContext.refreshUI();
        deleteTempZip(zipFilePath);
        return;
      }

      // 선택된 모델 타입 확인
      int selectedId = radioGroup.getCheckedRadioButtonId();
      String modelType = (selectedId == radioSpace.getId()) ? "space" : "object";

      // 업로드 실행
      uploadToFastApi(zipFilePath, groupName, modelType);
    });

    // 취소 버튼
    builder.setNegativeButton(mContext.getString(android.R.string.cancel), (dialog, which) -> {
      dialog.cancel();
      mContext.refreshUI();
      deleteTempZip(zipFilePath);
    });

    AlertDialog d = builder.create();
    if (d.getWindow() != null) {
      d.getWindow().setBackgroundDrawable(mContext.getDrawable(R.drawable.background_dialog));
    }
    d.show();
  }

  private void uploadToFastApi(String zipFilePath, String groupName, String modelType) {
    Log.d("FileAdapter", "FastAPI 업로드 시도: " + zipFilePath + ", 그룹: " + groupName + ", 타입: " + modelType);
    mContext.showProgress();

    FastApiService.FastApiInterface service =
            FastApiService.getRetrofitInstance(mContext).create(FastApiService.FastApiInterface.class);

    File file = new File(zipFilePath);
    RequestBody requestFile = RequestBody.create(MediaType.parse("application/zip"), file);
    MultipartBody.Part filePart = MultipartBody.Part.createFormData("file", file.getName(), requestFile);
    MultipartBody.Part groupNamePart = MultipartBody.Part.createFormData("group_name", groupName);

    // ★ modelType을 파라미터로 받아서 사용
    MultipartBody.Part modelTypePart = MultipartBody.Part.createFormData("model_type", modelType);

    Call<FastApiService.FastSuccessResponse> call = service.uploadFile(modelTypePart, groupNamePart, filePart);

    call.enqueue(new Callback<FastApiService.FastSuccessResponse>() {
      @Override
      public void onResponse(Call<FastApiService.FastSuccessResponse> call, Response<FastApiService.FastSuccessResponse> response) {
        mContext.refreshUI();
        deleteTempZip(zipFilePath);

        if (response.isSuccessful()) {
          FastApiService.FastSuccessResponse responseBody = response.body();
          if (responseBody != null && responseBody.isSuccess()) {
            String msg = "FastAPI 업로드 성공: " + responseBody.getData().getSavedFilename();
            Log.d("FileAdapter", msg);
            Toast.makeText(mContext, msg, Toast.LENGTH_LONG).show();
          } else {
            String msg = "FastAPI 업로드 성공 (서버 응답: " + (responseBody != null ? responseBody.getMessage() : "null") + ")";
            Log.d("FileAdapter", msg);
            Toast.makeText(mContext, msg, Toast.LENGTH_SHORT).show();
          }
        } else {
          String errorMsg = "FastAPI 응답 실패: " + response.code();
          try {
            String errorBodyStr = response.errorBody().string();
            errorMsg += ", " + errorBodyStr;
          } catch (Exception e) { e.printStackTrace(); }
          Log.e("FileAdapter", errorMsg);
          Toast.makeText(mContext, errorMsg, Toast.LENGTH_LONG).show();
        }
      }

      @Override
      public void onFailure(Call<FastApiService.FastSuccessResponse> call, Throwable t) {
        mContext.refreshUI();
        deleteTempZip(zipFilePath);
        String errorMsg = "FastAPI 요청 실패: " + t.getMessage();
        Log.e("FileAdapter", errorMsg, t);
        Toast.makeText(mContext, errorMsg, Toast.LENGTH_LONG).show();
      }
    });
  }

  private void deleteTempZip(String zipFilePath) {
    try {
      File tempZip = new File(zipFilePath);
      if (tempZip.exists()) {
        if (tempZip.delete()) {
          Log.d("FileAdapter", "임시 ZIP 파일 삭제 성공: " + zipFilePath);
        } else {
          Log.w("FileAdapter", "임시 ZIP 파일 삭제 실패: " + zipFilePath);
        }
      }
    } catch (Exception e) {
      Log.e("FileAdapter", "임시 ZIP 파일 삭제 중 오류", e);
    }
  }
}
