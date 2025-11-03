package com.snapspace.scanner.ui;

import android.app.AlertDialog;
import android.app.Dialog;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.drawable.BitmapDrawable;
import android.graphics.drawable.Drawable;
import android.net.Uri;
import android.os.Build;
import android.preference.PreferenceManager;
import android.util.Log; // <-- [추가] 로그 import
import android.view.LayoutInflater;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.widget.BaseAdapter;
import android.widget.GridView;
import android.widget.ImageView;
import android.widget.TextView;
import android.widget.Toast; // <-- [추가] Toast import

import androidx.core.content.FileProvider;
import com.snapspace.scanner.BuildConfig;


import com.snapspace.scanner.R;
import com.snapspace.scanner.main.Exporter;
import com.snapspace.scanner.main.Main;
import com.snapspace.scanner.sketchfab.OAuth;
import com.snapspace.scanner.fastapi.FastApiService; // <-- 'fastapi'가 'network'로 변경됨 (이전 코드 기준)
import com.lvonasek.utils.GestureDetector;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashMap;
import java.util.Locale;
import java.util.Scanner;

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
        int before = (int)mColumns;
        mColumns -= diff * 0.75f;
        if (mColumns < 2) {
          mColumns = 2;
        }
        if (mColumns > 5) {
          mColumns = 5;
        }
        int after = (int)mColumns;

        if (before != after) {
          mContext.setColumns(after);
        }
      }
    }, mContext);
  }

  // ... (getCount, getItem, getItemId, getView, hasParent, toParent, loadIcon, getPath... 등 다른 메소드는 동일) ...
  // ... (이하 기존 코드 생략) ...
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

    //set extension
    View extension = view.findViewById(R.id.extension);
    extension.setVisibility(key.endsWith(Exporter.EXT_DATASET) ? View.VISIBLE : view.GONE);

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
      } else if (key.endsWith(Exporter.EXT_DATASET)) {
        startPostprocess(key);
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

  private void startPostprocess(String key) {

    AlertDialog.Builder builder = new AlertDialog.Builder(mContext);
    builder.setView(R.layout.dialog_scan);
    Dialog dialog = builder.create();
    dialog.getWindow().setBackgroundDrawable(mContext.getDrawable(R.drawable.background_dialog));
    dialog.show();
    ((TextView)dialog.findViewById(R.id.name)).setText(R.string.export);


    ArrayList<Drawable> icons = new ArrayList<>();
    ArrayList<String> values = new ArrayList<>();
    values.add(mContext.getString(R.string.export_model));
    values.add(mContext.getString(R.string.export_floorplan));
    values.add(mContext.getString(R.string.export_pointcloud));
    icons.add(mContext.getDrawable(R.drawable.ic_type_scan));
    icons.add(mContext.getDrawable(R.drawable.ic_type_floorplan));
    icons.add(mContext.getDrawable(R.drawable.ic_type_pointcloud));

    SharedPreferences pref = PreferenceManager.getDefaultSharedPreferences(mContext);
    ArrayAdapterWithIcons adapter = new ArrayAdapterWithIcons(mContext, values, icons);
    GridView list = dialog.findViewById(R.id.list);
    list.setAdapter(adapter);
    list.setOnTouchListener((v, event) -> event.getAction() == MotionEvent.ACTION_MOVE);
    list.setOnItemClickListener((adapterView, view, index, l) -> {
      String mode = values.get(index);
      SharedPreferences.Editor e = pref.edit();
      e.putBoolean(mContext.getString(R.string.pref_later), true);
      if (mode.compareTo(mContext.getString(R.string.export_floorplan)) == 0) {
        e.putString(mContext.getString(R.string.pref_mode), "exp_floorplan");
      } else if (mode.compareTo(mContext.getString(R.string.export_pointcloud)) == 0) {
        e.putString(mContext.getString(R.string.pref_mode), "exp_pointcloud");
      } else if (mode.compareTo(mContext.getString(R.string.export_model)) == 0) {
        e.putString(mContext.getString(R.string.pref_mode), "realtime");
      }
      e.commit();

      File file = new File(getPath(), key);
      Intent intent = new Intent(mContext, Main.class);
      intent.putExtra(AbstractActivity.FILE_KEY, file.getAbsolutePath());
      dialog.dismiss();
      mContext.startActivity(intent);
    });
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
    } else if ((key.endsWith(Exporter.EXT_DATASET)) || (key.endsWith(Exporter.EXT_PLY))) {
      mContext.showProgress();
      new Thread(() -> {
        // .dataset 이나 .ply는 FastAPI로 보낼지, 다른 앱으로 보낼지 묻습니다.
        final String zip = Exporter.compressModel(new File(getPath(), key));
        mContext.runOnUiThread(() -> {
          mContext.showProgress(); // 프로그레스 숨기기
          showShareDialog(zip, key, false);
        });
      }).start();
    } else {
      // .obj 파일일 경우 (기존 로직)
      AlertDialog.Builder dialog = new AlertDialog.Builder(mContext);
      dialog.setTitle(R.string.share_via);
      // R.array.shares에 "FastAPI" 항목을 추가해야 합니다.
      // 여기서는 0: 기타 앱, 1: 온라인 변환기, 2: FastAPI (Sketchfab 대신)로 가정합니다.
      // strings.xml의 <string-array name="shares"> 항목 수정이 필요합니다.
      // 예: <item>FastAPI</item>
      dialog.setItems(R.array.shares, (dialog1, which) -> {
        mContext.showProgress();
        new Thread(() -> {
          File file = new File(getPath(), key);
          // '온라인 변환기'가 아니라면 항상 압축합니다.
          final String zip = which == 1 ? AbstractActivity.getModel(file).getAbsolutePath() : Exporter.compressModel(file);
          mContext.runOnUiThread(() -> {
            Intent intent;
            switch (which) {
              case 0: //intent (기타 앱 공유)
                intent = new Intent(Intent.ACTION_SEND);
                intent.setType("application/zip");
                intent.putExtra(Intent.EXTRA_STREAM, FileProvider.getUriForFile(mContext, BuildConfig.APPLICATION_ID + ".provider", new File(zip)));
                mContext.startActivity(Intent.createChooser(intent, mContext.getString(R.string.share_via)));
                break;
              case 1: //online (이건 zip이 아님, .obj 경로)
                intent = new Intent(mContext, Uploader.class);
                intent.putExtra(AbstractActivity.FILE_KEY, zip);
                intent.putExtra(AbstractActivity.URL_KEY, "https://anyconv.com/mesh-converter/");
                mContext.startActivity(intent);
                break;
              case 2: // [수정] FastAPI (Sketchfab 대신)
                uploadToFastApi(zip); // <--- 여기를 수정했습니다.
                break;
            }
            mContext.refreshUI(); // 업로드 후 새로고침은 불필요할 수 있음
          });
        }).start();
      });
      AlertDialog d = dialog.create();
      d.getWindow().setBackgroundDrawable(mContext.getDrawable(R.drawable.background_dialog));
      d.show();
    }
  }

  /**
   * [신규] .dataset과 .ply를 위한 공유 다이얼로그
   */
  private void showShareDialog(String zipFilePath, String key, boolean isObj) {
    AlertDialog.Builder builder = new AlertDialog.Builder(mContext);
    builder.setTitle(R.string.share_via);
    CharSequence[] items = new CharSequence[]{"FastAPI로 업로드", "기타 앱으로 공유"};

    builder.setItems(items, (dialog, which) -> {
      if (which == 0) {
        // "FastAPI로 업로드" 선택
        uploadToFastApi(zipFilePath);
      } else {
        // "기타 앱으로 공유" 선택
        Intent intent = new Intent(Intent.ACTION_SEND);
        intent.setType("application/zip");
        intent.putExtra(Intent.EXTRA_STREAM, FileProvider.getUriForFile(mContext, BuildConfig.APPLICATION_ID + ".provider", new File(zipFilePath)));
        mContext.startActivity(Intent.createChooser(intent, mContext.getString(R.string.share_via)));
      }
    });
    builder.setOnCancelListener(dialog -> {
      // 다이얼로그가 취소되면 임시 zip파일 삭제
      deleteTempZip(zipFilePath);
    });
    builder.show();
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

  // -----------------------------------------------------------------
  // --- [추가] FastAPI 업로드를 위한 신규 메소드 ---
  // -----------------------------------------------------------------

  /**
   * (신규 메소드) FastApiService를 사용하여 ZIP 파일을 POST로 전송합니다.
   *
   * @param zipFilePath 업로드할 ZIP 파일의 전체 경로
   */
  private void uploadToFastApi(String zipFilePath) {
    Log.d("FileAdapter", "FastAPI 업로드 시도: " + zipFilePath);
    mContext.showProgress(); // 업로드 중 프로그레스 바 표시

    // 1. FastApiService의 Retrofit 인스턴스와 인터페이스를 가져옵니다.
    FastApiService.FastApiInterface service =
            FastApiService.getRetrofitInstance().create(FastApiService.FastApiInterface.class);

    // 2. 파일(File) 객체를 생성합니다.
    File file = new File(zipFilePath);

    // 3. 파일을 MultipartBody.Part로 변환합니다.
    RequestBody requestFile = RequestBody.create(MediaType.parse("application/zip"), file);
    // "file"은 FastApiInterface에서 @Part("file")에 설정한 '이름'과 일치해야 합니다.
    MultipartBody.Part filePart = MultipartBody.Part.createFormData("file", file.getName(), requestFile);

    // 4. Retrofit Call 객체를 생성합니다.
    Call<FastApiService.FastSuccessResponse> call = service.uploadFile(filePart);

    // 5. 요청을 비동기(Asynchronous)로 실행합니다.
    call.enqueue(new Callback<FastApiService.FastSuccessResponse>() {
      @Override
      public void onResponse(Call<FastApiService.FastSuccessResponse> call, Response<FastApiService.FastSuccessResponse> response) {
        // 6. 응답이 메인 스레드로 돌아옵니다.
        mContext.showProgress(); // 프로그레스 바 숨기기
        deleteTempZip(zipFilePath); // 업로드 완료 후 임시 zip파일 삭제

        if (response.isSuccessful()) {
          // HTTP 200-299 범위: 성공
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
          // HTTP 4xx, 5xx 등: 실패
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
        // 7. 네트워크 오류, DNS 오류, 타임아웃 등
        mContext.showProgress(); // 프로그레스 바 숨기기
        deleteTempZip(zipFilePath); // 오류 발생 시 임시 zip파일 삭제
        String errorMsg = "FastAPI 요청 실패: " + t.getMessage();
        Log.e("FileAdapter", errorMsg, t);
        Toast.makeText(mContext, errorMsg, Toast.LENGTH_LONG).show();
      }
    });
  }

  /**
   * (신규 헬퍼 메소드) 임시 ZIP 파일을 삭제합니다.
   */
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

