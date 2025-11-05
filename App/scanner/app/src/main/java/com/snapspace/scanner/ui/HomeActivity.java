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
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;
import android.widget.Toast;

import com.snapspace.scanner.R;
import com.snapspace.scanner.main.Exporter;
import com.snapspace.scanner.main.Main;

import java.io.File;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;

public class HomeActivity extends AbstractActivity implements View.OnClickListener {

    // 맴버 변수 선언
    private LinearLayout mProcessLayout;
    private ProgressBar mProgress;
    private TextView mInfoText;
    private Button mCancelButton;
    private LinearLayout mButtonGird;
    private ImageView mSpaceScanButton;
    private ImageView mObjectScanButton;
    private Button mPreviewButton;
    private Button mUploadButton;

    private long backPressedTime = 0;

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
        mProcessLayout = findViewById(R.id.processing_layout);
        mProgress = findViewById(R.id.progressBar);
        mInfoText = findViewById(R.id.info_text);
        mCancelButton = findViewById(R.id.service_cancel);
        mButtonGird = findViewById(R.id.button_grid);
        mSpaceScanButton = findViewById(R.id.space_scan_button);
        mObjectScanButton = findViewById(R.id.object_scan_button);
        mPreviewButton = findViewById(R.id.preview_button);
        mUploadButton = findViewById(R.id.upload_button);

        // 버튼 리스너 등록
        mSpaceScanButton.setOnClickListener(this);
        mObjectScanButton.setOnClickListener(this);
        mPreviewButton.setOnClickListener(this);
        mUploadButton.setOnClickListener(this);
        mCancelButton.setOnClickListener(this);

        // 버튼 초기 세팅
        mProgress.setVisibility(View.GONE);
        mInfoText.setVisibility(View.GONE);
        mCancelButton.setVisibility(View.GONE);
        mButtonGird.setVisibility(View.VISIBLE);
        mSpaceScanButton.setVisibility(View.VISIBLE);
        mObjectScanButton.setVisibility(View.VISIBLE);
        mPreviewButton.setVisibility(View.VISIBLE);
        mUploadButton.setVisibility(View.VISIBLE);

        // 권환 확인
        checkPermissions();
    }

    @Override
    protected void onResume() {
        super.onResume();

        // 저장 완료 상태 확인
        int serviceState = Service.getRunning(this);
        if (serviceState < 0) {
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

        // 공간 스캔 버튼 클릭
        if (id == R.id.space_scan_button) {
            startScanning("space_scan");
        }
        // 오브젝트 스캔 버튼 클릭
        else if (id == R.id.object_scan_button) {
            startScanning("object_scan");
        }
        // 스캔 결과 보기 버튼 클릭
        else if (id == R.id.preview_button) {
            startActivity(new Intent(HomeActivity.this, FileManager.class));
        }
        // 서버 업로드 버튼 클릭
        else if (id == R.id.upload_button) {
            Toast.makeText(HomeActivity.this, "서버 업로드 기능 준비 중...", Toast.LENGTH_SHORT).show();
        }
        // 작업 취소 버튼 클릭
        else if (id == R.id.service_cancel) {
            cancelProcessing();
        }
        // 잘못된 입력 값
        else {
            Toast.makeText(HomeActivity.this, "입력이 잘못되었습니다.", Toast.LENGTH_SHORT).show();
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

        showProgress();

        startActivity(new Intent(HomeActivity.this, Main.class));
    }

    private void finishScanning()
    {
        showProgress();
        Date date = new Date() ;
        SimpleDateFormat dateFormat = new SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US);
        final String filename = dateFormat.format(date);
        String text = getString(R.string.data_saved) + " " + filename;
        Toast.makeText(this, text, Toast.LENGTH_LONG).show();

        new Thread(() -> {
            // OBJ 파일 처리
            File objFile = new File(Service.getLink(HomeActivity.this));
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

            // 임시 디렉토리 삭제
            if (!isPostProcessLaterOn(HomeActivity.this)) {
                deleteRecursive(new File(objFile.getParent()));
                Log.d(TAG, "임시 디렉토리를 삭제하였습니다");
            }

            // 최종 정리
            Service.reset(HomeActivity.this);
            Intent intent = new Intent(HomeActivity.this, Main.class);
            intent.putExtra(FILE_KEY, objFileSaved.getAbsolutePath());
            showProgress();
            startActivity(intent);
        }).start();
    }

    public void showProgress()
    {
        try {
            mProcessLayout.setVisibility(View.VISIBLE);
            mProgress.setVisibility(View.VISIBLE);
            mInfoText.setVisibility(View.VISIBLE);
            mCancelButton.setVisibility(View.VISIBLE);
            mButtonGird.setVisibility(View.GONE);
            mSpaceScanButton.setVisibility(View.GONE);
            mObjectScanButton.setVisibility(View.GONE);
            mPreviewButton.setVisibility(View.GONE);
            mUploadButton.setVisibility(View.GONE);
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
