package com.example.myapplication

import android.Manifest
import android.content.ContentValues
import android.content.pm.PackageManager
import android.net.Uri
import android.opengl.GLSurfaceView
import android.os.Build
import android.os.Bundle
import android.provider.MediaStore
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.compose.ui.zIndex
import androidx.core.content.ContextCompat
import com.google.ar.core.ArCoreApk
import com.google.ar.core.Config
import com.google.ar.core.RecordingConfig
import com.google.ar.core.Session
import com.google.ar.core.exceptions.*
import java.text.SimpleDateFormat
import java.util.*
import java.util.concurrent.atomic.AtomicReference

class MainActivity : ComponentActivity() {

    // --- ARCore/Recording 상태 ---
    private var session: Session? = null
    private var isRecording by mutableStateOf(false)
    private var datasetUri: Uri? = null

    // --- GL / Renderer ---
    private var glView: GLSurfaceView? = null
    private lateinit var renderer: SimpleGlRenderer
    private val sessionRef = AtomicReference<Session?>(null)

    // --- Compose 상태 공유용 ---
    private var _status by mutableStateOf("대기 중")
    private fun setStatus(msg: String) { _status = msg }

    // 카메라 권한 런처
    private val cameraPermissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted ->
        if (!granted) setStatus("카메라 권한이 필요합니다.")
        else ensureArCoreInstalledAndCreateSession()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            MaterialTheme {
                // ===== 카메라 미리보기(GL) + 카메라 크롬(Compose) =====
                Box(Modifier.fillMaxSize()) {

                    // 1) 카메라 미리보기 (GLSurfaceView)
                    AndroidView(
                        modifier = Modifier.fillMaxSize().zIndex(0f),
                        factory = { ctx ->
                            GLSurfaceView(ctx).apply {
                                setEGLContextClientVersion(3) // GLES3
                                preserveEGLContextOnPause = true
                                renderer = SimpleGlRenderer(
                                    context = ctx,
                                    status = { msg -> runOnUiThread { setStatus(msg) } },
                                    sessionProvider = { sessionRef.get() }
                                )
                                this@MainActivity.renderer = renderer
                                setRenderer(renderer)
                                renderMode = GLSurfaceView.RENDERMODE_CONTINUOUSLY
                                glView = this
                            }
                        }
                    )

                    // 2) 카메라 앱 같은 UI 크롬(상단/하단/상태)
                    CameraChrome(
                        isRecording = isRecording,
                        statusText = _status,
                        onClickShutter = {
                            setStatus("셔터 탭됨")
                            if (!isRecording) startRecordingFlow() else stopRecordingFlow()
                        },
                        onClickSettings = { /* TODO: 설정 */ },
                        onClickSwitchCamera = { /* TODO: 전후면 전환(ARCore는 후면 권장) */ }
                    )
                }
            }
        }
    }

    override fun onResume() {
        super.onResume()
        glView?.onResume() // GL 컨텍스트 복구
        // 권한 확인 -> 세션 준비
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA)
            != PackageManager.PERMISSION_GRANTED) {
            cameraPermissionLauncher.launch(Manifest.permission.CAMERA)
        } else {
            ensureArCoreInstalledAndCreateSession()
        }
    }

    override fun onPause() {
        super.onPause()
        glView?.onPause()
        session?.pause()
    }

    // ------------------ ARCore 세션 준비 ------------------
    private fun ensureArCoreInstalledAndCreateSession() {
        if (session != null) {
            try {
                session?.resume()
            } catch (e: CameraNotAvailableException) {
                setStatus("카메라 사용 불가: ${e.message}")
            }
            return
        }

        // ARCore 설치 확인/요청
        try {
            val installStatus = ArCoreApk.getInstance().requestInstall(this, true)
            if (installStatus == ArCoreApk.InstallStatus.INSTALL_REQUESTED) {
                setStatus("ARCore 설치 진행 중…")
                return
            }
        } catch (e: UnavailableUserDeclinedInstallationException) {
            setStatus("사용자가 ARCore 설치를 거부했습니다.")
            return
        } catch (e: Exception) {
            setStatus("ARCore 설치 확인 오류: ${e.message}")
            return
        }

        // 세션 생성
        try {
            session = Session(this)
        } catch (e: UnavailableArcoreNotInstalledException) {
            setStatus("ARCore가 설치되어 있지 않습니다.")
            return
        } catch (e: UnavailableDeviceNotCompatibleException) {
            setStatus("이 기기는 ARCore를 지원하지 않습니다.")
            return
        } catch (e: Exception) {
            setStatus("세션 생성 실패: ${e.message}")
            return
        }

        // Config 적용
        val config = Config(session).apply {
            depthMode = Config.DepthMode.AUTOMATIC // 다음 단계에 RAW_DEPTH_ONLY로 바꿀 예정
            focusMode = Config.FocusMode.AUTO
        }
        session!!.configure(config)

        // renderer가 사용할 수 있도록 세션 공유
        sessionRef.set(session)

        try {
            session!!.resume()
            setStatus("세션 시작됨")
        } catch (e: CameraNotAvailableException) {
            setStatus("카메라 사용 불가: ${e.message}")
        }
    }

    // ------------------ 녹화 시작 ------------------
    private fun startRecordingFlow() {
        if (isRecording) {
            setStatus("이미 녹화 중입니다.")
            return
        }
        val s = session ?: run {
            setStatus("세션이 아직 준비되지 않았습니다.")
            return
        }

        // MediaStore에 Movies/ARCore 하위에 항목을 먼저 생성
        val fileName = "ARCore_" + SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US).format(Date()) + ".mp4"
        val cv = ContentValues().apply {
            put(MediaStore.MediaColumns.DISPLAY_NAME, fileName)
            put(MediaStore.MediaColumns.MIME_TYPE, "video/mp4")
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                put(MediaStore.MediaColumns.RELATIVE_PATH, "Movies/ARCore") // 갤러리 폴더
                put(MediaStore.MediaColumns.IS_PENDING, 1)
            }
        }
        val collection = MediaStore.Video.Media.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY)
        val uri = contentResolver.insert(collection, cv)
        if (uri == null) {
            setStatus("MediaStore 항목 생성 실패")
            return
        }
        datasetUri = uri

        // ARCore RecordingConfig 구성(데이터셋 MP4)
        val recCfg = RecordingConfig(s).apply {
            setMp4DatasetUri(uri)
            setAutoStopOnPause(true)
        }

        try {
            s.startRecording(recCfg)
            isRecording = true
            setStatus("녹화 시작: $fileName")
        } catch (e: Exception) {
            setStatus("녹화 시작 오류: ${e.message}")
            safeFinalizePending(false)
        }
    }

    // ------------------ 녹화 중지 ------------------
    private fun stopRecordingFlow() {
        if (!isRecording) {
            setStatus("녹화 중이 아닙니다.")
            return
        }
        val s = session ?: return
        try {
            s.stopRecording()
            isRecording = false
            setStatus("녹화 중지됨")
            safeFinalizePending(true)   // 갤러리에 노출되도록 커밋
        } catch (e: Exception) {
            setStatus("녹화 중지 오류: ${e.message}")
            safeFinalizePending(false)
        }
    }

    // MediaStore IS_PENDING 해제(갤러리 노출)
    private fun safeFinalizePending(success: Boolean) {
        val uri = datasetUri ?: return
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            val cv = ContentValues().apply { put(MediaStore.MediaColumns.IS_PENDING, 0) }
            try { contentResolver.update(uri, cv, null, null) }
            catch (e: Exception) { runOnUiThread { setStatus("MediaStore 커밋 오류: ${e.message}") } }
        }
        datasetUri = null
    }
}

/* ======================= Compose 카메라 크롬 ======================= */

@Composable
private fun CameraChrome(
    isRecording: Boolean,
    statusText: String,
    onClickShutter: () -> Unit,
    onClickSettings: () -> Unit,
    onClickSwitchCamera: () -> Unit
) {
    // 상단 바 + 하단 바를 오버레이
    Box(Modifier.fillMaxSize()) {

        // 상단 바 (좌: 설정, 우: 상태 텍스트 간단 표시)
        TopBar(
            statusText = statusText,
            onClickSettings = onClickSettings
        )

        // 하단 바 (좌: 썸네일 자리, 중앙: 셔터, 우: 카메라 전환)
        BottomBar(
            isRecording = isRecording,
            onClickShutter = onClickShutter,
            onClickSwitchCamera = onClickSwitchCamera
        )
    }
}

@Composable
private fun TopBar(
    statusText: String,
    onClickSettings: () -> Unit
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = 16.dp, start = 16.dp, end = 16.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        // 좌측: 설정(임시 버튼)
        TextButton(onClick = onClickSettings) { Text("설정") }

        // 우측: 상태 텍스트(간략)
        Surface(
            color = Color(0x66000000),
            shape = MaterialTheme.shapes.small
        ) {
            Text(
                text = statusText,
                color = Color.White,
                modifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp)
            )
        }
    }
}

@Composable
private fun BottomBar(
    isRecording: Boolean,
    onClickShutter: () -> Unit,
    onClickSwitchCamera: () -> Unit
) {
    val shutterSize = with(LocalDensity.current) { 84.dp }  // 갤럭시 유사 큰 셔터

    Box(
        Modifier.fillMaxSize()
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(bottom = 24.dp, start = 24.dp, end = 24.dp)
                .align(Alignment.BottomCenter),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            // 좌측: 최근 썸네일 자리(임시)
            Box(
                Modifier
                    .size(48.dp)
                    .clip(MaterialTheme.shapes.small)
                    .background(Color(0x80000000)),
                contentAlignment = Alignment.Center
            ) {
                Text("갤러리", color = Color.White)
            }

            // 중앙: 셔터 버튼 (녹화 중이면 빨간색)
            Box(
                modifier = Modifier
                    .size(shutterSize)
                    .clip(CircleShape)
                    .background(if (isRecording) Color(0xFFE53935) else Color.White)
                    .padding(6.dp)
                    .clip(CircleShape)
                    .background(Color(0x33000000))
                    .zIndex(2f)
                    .clickable { onClickShutter() },   // ← 여기에 클릭 부여
            ) {
                // 터치 영역
                Box(
                    modifier = Modifier
                        .fillMaxSize()
                        .clip(CircleShape)
                        .background(Color.Transparent)
                )
            }
            // 클릭은 Row 전체에서 손실되지 않도록 Button 별도 제공
            Button(
                onClick = onClickShutter,
                modifier = Modifier
                    .size(shutterSize)
                    .align(Alignment.CenterVertically),
                colors = ButtonDefaults.buttonColors(
                    containerColor = Color.Transparent
                ),
                contentPadding = PaddingValues(0.dp)
            ) { /* 비워두고, 위의 원이 시각 */ }

            // 우측: 카메라 전환(ARCore는 후면 권장, UI만)
            TextButton(onClick = onClickSwitchCamera) { Text("전환") }
        }
    }
}
