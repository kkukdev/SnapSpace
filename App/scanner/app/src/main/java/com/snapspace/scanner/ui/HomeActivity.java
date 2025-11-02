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
import android.widget.ProgressBar;
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
    private ProgressBar mProgress;

    // onCreate 메서드
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_home);

        // UI 컴포넌트 초기화
        mProgress = findViewById(R.id.progressBar);

        // 버튼 리스너 등록
        findViewById(R.id.space_scan_button).setOnClickListener(this);
        findViewById(R.id.object_scan_button).setOnClickListener(this);
        findViewById(R.id.preview_button).setOnClickListener(this);
        findViewById(R.id.upload_button).setOnClickListener(this);

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
                startActivity(new Intent(this, Main.class));  // ← FileManager처럼!
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
            Toast.makeText(HomeActivity.this, "오브젝트 스캔 기능 준비 중...", Toast.LENGTH_SHORT).show();
        }
        // 스캔 미리보기 버튼 클릭
        else if (id == R.id.preview_button) {
            Toast.makeText(HomeActivity.this, "스캔 미리보기 기능 준비 중...", Toast.LENGTH_SHORT).show();
        }
        // 서버 업로드 버튼 클릭
        else if (id == R.id.upload_button) {
            Toast.makeText(HomeActivity.this, "서버 업로드 기능 준비 중...", Toast.LENGTH_SHORT).show();
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
            File file = new File(Service.getLink(HomeActivity.this));
            File file2save = Exporter.export(file, filename);

            Exporter.makeStructure(AbstractActivity.getPath(false));

            File plyFile = new File(file.getParent(), "pointcloud.ply");
            if (plyFile.exists()) {
                File objFolderPath = new File(AbstractActivity.getPath(false), filename + Exporter.EXT_OBJ);
                File plyDestination = new File(objFolderPath, "pointcloud.ply");
                if (plyFile.renameTo(plyDestination)) {
                    Log.d(TAG, "PLY file " + plyDestination.toString() + " moved to OBJ folder");
                }
            }

            //remove temp dir
            if (!isPostProcessLaterOn(HomeActivity.this))
                deleteRecursive(new File(file.getParent()));

            //finish
            Service.reset(HomeActivity.this);
            Intent intent = new Intent(HomeActivity.this, Main.class);
            intent.putExtra(FILE_KEY, file2save.getAbsolutePath());
            showProgress();
            startActivity(intent);
        }).start();
    }

    public void showProgress()
    {
        try {
            mProgress.setVisibility(View.VISIBLE);
        } catch (Exception e) {
            e.printStackTrace();
        }
    }
}
