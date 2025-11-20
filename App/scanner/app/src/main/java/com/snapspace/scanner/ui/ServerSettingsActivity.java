package com.snapspace.scanner.ui;

import android.graphics.Color;
import android.os.Bundle;
import android.view.View;
import android.widget.Button;
import android.widget.EditText;
import android.widget.ImageButton;
import android.widget.Toast;

import com.snapspace.scanner.R;
import com.snapspace.scanner.utils.ServerConfigManager;

public class ServerSettingsActivity extends AbstractActivity implements View.OnClickListener {

    private EditText editServerIp;
    private EditText editServerPort;
    private Button btnCancel;
    private Button btnSave;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_server_settings);

        // UI 컴포넌트 초기화
        editServerIp = findViewById(R.id.edit_server_ip);
        editServerPort = findViewById(R.id.edit_server_port);
        btnCancel = findViewById(R.id.btn_cancel);
        btnSave = findViewById(R.id.btn_save);

        // 리스너 등록
        btnCancel.setOnClickListener(this);
        btnSave.setOnClickListener(this);

        // 현재 설정값 불러오기
        loadCurrentSettings();
    }

    /**
     * 현재 저장된 서버 설정을 불러와서 표시
     */
    private void loadCurrentSettings() {
        String currentIp = ServerConfigManager.getServerIp(this);
        String currentPort = ServerConfigManager.getServerPort(this);

        editServerIp.setText(currentIp);
        editServerPort.setText(currentPort);
    }

    @Override
    public void onClick(View v) {
        int id = v.getId();

        if (id == R.id.btn_cancel) {
            finish();
        }
        else if (id == R.id.btn_save) {
            saveSettings();
        }
    }

    /**
     * 입력된 서버 설정 저장
     */
    private void saveSettings() {
        String ip = editServerIp.getText().toString().trim();
        String port = editServerPort.getText().toString().trim();

        android.util.Log.d("ServerSettings", "저장 시도 - IP: " + ip + ", Port: " + port);

        // 입력값 검증
        if (ip.isEmpty()) {
            Toast.makeText(this, "IP 주소를 입력해주세요", Toast.LENGTH_SHORT).show();
            editServerIp.requestFocus();
            return;
        }

        if (!ServerConfigManager.isValidIp(ip)) {
            Toast.makeText(this, "올바른 IP 주소를 입력해주세요\n예: 192.168.0.100", Toast.LENGTH_SHORT).show();
            editServerIp.requestFocus();
            return;
        }

        if (port.isEmpty()) {
            Toast.makeText(this, "포트 번호를 입력해주세요", Toast.LENGTH_SHORT).show();
            editServerPort.requestFocus();
            return;
        }

        if (!ServerConfigManager.isValidPort(port)) {
            Toast.makeText(this, "올바른 포트 번호를 입력해주세요\n(1-65535)", Toast.LENGTH_SHORT).show();
            editServerPort.requestFocus();
            return;
        }

        // 설정 저장
        ServerConfigManager.saveConfig(this, ip, port);

        android.util.Log.d("ServerSettings", "저장 완료 - IP: " + ip + ", Port: " + port);
        String savedUrl = ServerConfigManager.getServerUrl(this);
        android.util.Log.d("ServerSettings", "저장 후 확인 URL: " + savedUrl);

        Toast.makeText(this, "서버 설정이 저장되었습니다\n" + savedUrl, Toast.LENGTH_LONG).show();
        finish();
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