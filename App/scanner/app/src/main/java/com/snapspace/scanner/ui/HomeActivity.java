package com.snapspace.scanner.ui;

import android.Manifest;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.os.Bundle;
import android.preference.PreferenceManager;
import android.util.Log;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;
import android.widget.Toast;

import androidx.viewpager2.widget.ViewPager2;

import com.snapspace.scanner.R;
import com.snapspace.scanner.main.Exporter;
import com.snapspace.scanner.main.Main;

import java.io.File;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Locale;

public class HomeActivity extends AbstractActivity implements View.OnClickListener {

    // 맴버 변수 선언
    private LinearLayout mProcessLayout;
    private ProgressBar mProgress;
    private TextView mInfoText;
    private Button mCancelButton;

    private LinearLayout mMainLayout;
    private LinearLayout mHeaderLayout;
    private LinearLayout mCardViewLayout;

    private long backPressedTime = 0;

    // 버튼들 (LinearLayout으로 변경)
    private LinearLayout btnSpaceScan;
    private LinearLayout btnObjectScan;
    private LinearLayout btnPreview;
    private LinearLayout btnSettings;

    @Override
    public void onBackPressed() {
        if (System.currentTimeMillis() - backPressedTime < 2000) {
            // 2초 이내 두 번 누르면 종료
            finish();
            finishAffinity();
        } else {
            backPressedTime = System.currentTimeMillis();
            Toast.makeText(this, "한 번 더 누르면 종료됩니다", Toast.LENGTH_SHORT).show();
        }
    }

    // onCreate 메서드
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_home);

        // UI 컴포넌트 초기화
        mMainLayout = findViewById(R.id.main_layout);
        mHeaderLayout = findViewById(R.id.header);
        mCardViewLayout = findViewById(R.id.card_view_layout);

        // 버튼 초기화
        btnSpaceScan = findViewById(R.id.btn_space_scan);
        btnObjectScan = findViewById(R.id.btn_object_scan);
        btnPreview = findViewById(R.id.btn_preview);
        btnSettings = findViewById(R.id.btn_settings);

        mProcessLayout = findViewById(R.id.processing_layout);
        mProgress = findViewById(R.id.progressBar);
        mInfoText = findViewById(R.id.info_text);
        mCancelButton = findViewById(R.id.service_cancel);

        // 버튼 리스너 등록
        btnSpaceScan.setOnClickListener(this);
        btnObjectScan.setOnClickListener(this);
        btnPreview.setOnClickListener(this);
        btnSettings.setOnClickListener(this);
        mCancelButton.setOnClickListener(this);

        // 초기 세팅
        mHeaderLayout.setVisibility(View.VISIBLE);
        mCardViewLayout.setVisibility(View.VISIBLE);
        mProgress.setVisibility(View.GONE);
        mInfoText.setVisibility(View.GONE);
        mCancelButton.setVisibility(View.GONE);

        // 권한 확인
        checkPermissions();
    }

     @Override
     protected void onResume() {
         super.onResume();

         // 서비스 실행 중 상태 확인
         int serviceState = Service.getRunning(this);
         
         // 서비스가 현재 실행 중일 때 (양수)
         if (serviceState > Service.SERVICE_NOT_RUNNING) {
             showProgress();
             mInfoText.setText("");
             
             // 1초마다 진행 상태 업데이트
             new Thread(() -> {
                 while(Service.getRunning(this) > Service.SERVICE_NOT_RUNNING) {
                     try {
                         Thread.sleep(1000);
                     } catch (Exception e) {
                         e.printStackTrace();
                     }
                     
                     runOnUiThread(() -> {
                         String message = Service.getMessage();
                         if (message == null) {
                             mInfoText.setText(getString(R.string.failed));
                         } else {
                             mInfoText.setText(getString(R.string.working) + "\n\n" + message);
                         }
                     });
                 }
             }).start();
         }
         // 서비스 완료 상태 확인 (음수)
         else if (serviceState < Service.SERVICE_NOT_RUNNING) {
             int absState = Math.abs(serviceState);

             if (absState == Service.SERVICE_SAVE) {
                 showProgress();
                 startActivity(new Intent(this, Main.class));
             }
             else if (absState == Service.SERVICE_POSTPROCESS) {
                 finishScanning();
             }
         }
     }

    // 권한 체크
    private void checkPermissions() {
        String[] permissions = {
                Manifest.permission.CAMERA,
        };

        boolean ok = true;
        for (String s : permissions) {
            if (checkSelfPermission(s) != PackageManager.PERMISSION_GRANTED) {
                ok = false;
                break;
            }
        }

        // 권한이 없으면 요청
        if (!ok) {
            requestPermissions(permissions, 1);
        }
    }

    // 권한 요청 결과 처리
    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == 1) {
            boolean ok = true;
            for (int result : grantResults) {
                if (result != PackageManager.PERMISSION_GRANTED) {
                    ok = false;
                    break;
                }
            }

            if (!ok) {
                Toast.makeText(this, "카메라 권한이 필요합니다", Toast.LENGTH_SHORT).show();
            }
        }
    }

    // 상태바 색상 설정
    @Override
    public int getStatusBarColor() {
        return Color.BLACK;
    }

    @Override
    public int getNavigationBarColor() {
        return getStatusBarColor();
    }

    // onClick 메서드 (버튼 클릭 메서드)
    @Override
    public void onClick(View v) {
        int id = v.getId();

        if (id == R.id.btn_space_scan) {
            startScanning("space_scan");
        }
        else if (id == R.id.btn_object_scan) {
            startScanning("object_scan");
        }
        else if (id == R.id.btn_preview) {
            startActivity(new Intent(this, FileManager.class));
        }
        else if (id == R.id.btn_settings) {
            // 설정 화면으로 이동
            startActivity(new Intent(this, SettingsActivity.class));
        }
        else if (id == R.id.service_cancel) {
            cancelProcessing();
        }
    }

    // 스캔 시작 메서드
    private void startScanning(String mode)
    {
        SharedPreferences pref = PreferenceManager.getDefaultSharedPreferences(HomeActivity.this);
        SharedPreferences.Editor e = pref.edit();

        e.putBoolean(getString(R.string.pref_later), false);
        e.putString(getString(R.string.pref_mode), "realtime");

        e.putString("scan_mode", mode);

        e.commit();

        startActivity(new Intent(HomeActivity.this, Main.class));
    }

     private void finishScanning()
     {
         showProgress();

         Date date = new Date() ;
         SimpleDateFormat dateFormat = new SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US);
        
         final String filename = dateFormat.format(date);
         
         // 토스트 메시지 표시 (텍스처 생성 완료 후)
         String text = getString(R.string.data_saved) + " " + filename;
         Toast.makeText(this, text, Toast.LENGTH_LONG).show();

         new Thread(() -> {
             // OBJ 파일 처리
             String link = Service.getLink(HomeActivity.this);
             Log.d(TAG, "Service.getLink() 반환 값: " + link);
             
             File objFile = new File(link);
             if (!objFile.exists()) {
                 Log.e(TAG, "파일이 존재하지 않습니다: " + objFile.getAbsolutePath());
                 runOnUiThread(() -> {
                     Toast.makeText(HomeActivity.this, "파일을 찾을 수 없습니다", Toast.LENGTH_LONG).show();
                     Service.reset(HomeActivity.this);
                 });
                 return;
             }
             
             File objFileSaved = Exporter.export(objFile, filename);
             Log.d(TAG, "OBJ 파일 처리를 완료했습니다: " + objFileSaved.getAbsolutePath());

             // PLY 파일 처리
             File plyFile = new File(objFile.getParent(), "pointcloud.ply");
             if (plyFile.exists()) {
                 File plyFileSaved = Exporter.export(plyFile, filename);
                 Log.d(TAG, "PLY 파일 처리를 완료했습니다: " + plyFileSaved.getAbsolutePath());
             } else {
                 Log.d(TAG, "PLY 파일을 찾을 수 없습니다: " + plyFile.getAbsolutePath());
             }

             // 폴더 구조 정렬
             Exporter.makeStructure(AbstractActivity.getPath(false));
             
             // 최종 정리
             Service.reset(HomeActivity.this);
             Intent intent = new Intent(HomeActivity.this, Main.class);
             intent.putExtra(FILE_KEY, objFileSaved.getAbsolutePath());
             startActivity(intent);
         }).start();
     }

    public void showProgress()
    {
        try {
            mHeaderLayout.setVisibility(View.GONE);
            mCardViewLayout.setVisibility(View.GONE);
            mProcessLayout.setVisibility(View.VISIBLE);
            mProgress.setVisibility(View.VISIBLE);
            mInfoText.setVisibility(View.VISIBLE);
            mCancelButton.setVisibility(View.VISIBLE);
        } catch (Exception e) {
            e.printStackTrace();
        }
    }

    // 작업 취소 메서드
    private void cancelProcessing() {
        // 사용자에게 확인 요청
        new android.app.AlertDialog.Builder(this)
                .setTitle(R.string.app_name)
                .setMessage("작업을 취소하시겠습니까?")
                .setPositiveButton("예", (dialog, which) -> {
                    // Service 중단 및 리셋
                    Service.interrupt();  // 메시지 중단
                    Service.reset(this);  // 상태 초기화

                    Toast.makeText(this, "작업이 취소되었습니다", Toast.LENGTH_SHORT).show();

                    // 앱 종료 (백그라운드 작업도 강제 종료)
                    System.exit(0);
                })
                .setNegativeButton("아니오", null)
                .show();
    }
}
