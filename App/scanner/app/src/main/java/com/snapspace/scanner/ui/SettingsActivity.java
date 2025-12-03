package com.snapspace.scanner.ui;

import android.content.Intent;
import android.graphics.Color;
import android.os.Bundle;
import android.view.View;
import android.widget.LinearLayout;
import android.widget.Toast;

import com.snapspace.scanner.R;

public class SettingsActivity extends AbstractActivity implements View.OnClickListener {
    private LinearLayout btnServerSettings;
    private LinearLayout btnTutorial;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_settings);

        // UI 컴포넌트 초기화
        btnServerSettings = findViewById(R.id.btn_server_settings);
        btnTutorial = findViewById(R.id.btn_tutorial);

        // 리스너 등록
        btnServerSettings.setOnClickListener(this);
        btnTutorial.setOnClickListener(this);
    }

    @Override
    public void onClick(View v) {
        int id = v.getId();

        if (id == R.id.btn_server_settings) {
            // 서버 설정 화면으로 이동
            startActivity(new Intent(this, ServerSettingsActivity.class));
        }
        else if (id == R.id.btn_tutorial) {
            // 튜토리얼 화면 (나중에 구현)
            Toast.makeText(this, "튜토리얼 기능은 준비 중입니다", Toast.LENGTH_SHORT).show();
        }
    }

    @Override
    public int getStatusBarColor() {
        return Color.BLACK;
    }

    @Override
    public int getNavigationBarColor() {
        return getStatusBarColor();
    }
}