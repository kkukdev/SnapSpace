package com.example.myapplication

import android.content.Context
import android.hardware.display.DisplayManager
import android.opengl.*
import android.os.Build
import android.view.Display
import android.view.Surface
import android.view.WindowManager
import com.google.ar.core.Coordinates2d
import com.google.ar.core.Frame
import com.google.ar.core.Session
import com.google.ar.core.exceptions.CameraNotAvailableException
import javax.microedition.khronos.egl.EGLConfig
import javax.microedition.khronos.opengles.GL10

/**
 * ARCore 카메라 프리뷰를 전체 화면에 렌더링하는 최소 렌더러.
 * - OES 텍스처 생성 → session.setCameraTextureNames(textureId)
 * - onDrawFrame: session.update() → 풀스크린 사각형에 OES 텍스처 그리기
 */
class SimpleGlRenderer(
    private val context: Context,
    private val status: (String) -> Unit,
    private val sessionProvider: () -> Session?
) : GLSurfaceView.Renderer {
    private var textureId: Int = -1
    private var program: Int = -1
    private var attribPosition: Int = -1
    private var attribTexCoord: Int = -1
    private var uniformTex: Int = -1

    // 풀스크린 정점(삼각형 스트립)
    private val quadCoords = floatArrayOf(
        -1f, -1f,
        1f, -1f,
        -1f,  1f,
        1f,  1f
    )

    // 텍스처 좌표 (후에 frame.transformDisplayUvCoords로 교정)
    private val quadTexCoords = floatArrayOf(
        0f, 1f,  // left-bottom
        1f, 1f,  // right-bottom
        0f, 0f,  // left-top
        1f, 0f   // right-top
    )
    private val texBuf = GLUtils.makeFloatBuffer(quadTexCoords)
    private val posBuf = GLUtils.makeFloatBuffer(quadCoords)

    private val ndcQuad = floatArrayOf(
        -1f, -1f,  1f, -1f,
        -1f,  1f,  1f,  1f
    )
    private val ndcBuf   = GLUtils.makeFloatBuffer(ndcQuad)                 // 입력(NDC)
    private val uvOutBuf = GLUtils.makeFloatBuffer(FloatArray(ndcQuad.size))// 출력(텍스처 정규화)

    override fun onSurfaceCreated(gl: GL10?, config: EGLConfig?) {
        GLES20.glClearColor(0f, 0f, 0f, 1f)

        // 1) OES 텍스처 생성
        textureId = GLUtils.createOesTexture()

        // 2) 셰이더/프로그램 생성
        program = GLUtils.buildProgram(VERT, FRAG_OES)
        attribPosition = GLES20.glGetAttribLocation(program, "aPosition")
        attribTexCoord = GLES20.glGetAttribLocation(program, "aTexCoord")
        uniformTex     = GLES20.glGetUniformLocation(program, "uTexture")

        // 3) 세션에게 우리가 만든 텍스처를 사용하라고 알림
        sessionProvider()?.setCameraTextureNames(intArrayOf(textureId))
    }

    override fun onSurfaceChanged(gl: GL10?, width: Int, height: Int) {
        GLES20.glViewport(0, 0, width, height)

        // ✅ Android 11(API 30) 이상에서는 DisplayManager 사용
        val rotation = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            val display: Display? = context.display
                ?: context.getSystemService(DisplayManager::class.java).getDisplay(Display.DEFAULT_DISPLAY)
            display?.rotation ?: Surface.ROTATION_0
        } else {
            // ✅ 하위 버전 호환: WindowManager 방식 유지
            @Suppress("DEPRECATION")
            (context.getSystemService(Context.WINDOW_SERVICE) as WindowManager)
                .defaultDisplay.rotation
        }

        sessionProvider()?.setDisplayGeometry(rotation, width, height)
    }

    override fun onDrawFrame(gl: GL10?) {
        GLES20.glClear(GLES20.GL_COLOR_BUFFER_BIT or GLES20.GL_DEPTH_BUFFER_BIT)

        val session = sessionProvider() ?: return
        try {
            val frame: Frame = session.update()

            // (선택) 매 프레임 재계산이 과하면 hasDisplayGeometryChanged()로 조건부 수행
            // if (frame.hasDisplayGeometryChanged()) { ... }

            // 입력: OPENGL NDC → 출력: TEXTURE_NORMALIZED
            ndcBuf.rewind()
            uvOutBuf.clear()
            frame.transformCoordinates2d(
                Coordinates2d.OPENGL_NORMALIZED_DEVICE_COORDINATES,
                ndcBuf,
                Coordinates2d.TEXTURE_NORMALIZED,
                uvOutBuf
            )

            // 우리가 그릴 텍스처 좌표 버퍼 갱신
            texBuf.clear()
            uvOutBuf.rewind()
            texBuf.put(uvOutBuf).position(0)

            // 드로우 (동일)
            GLES20.glUseProgram(program)
            GLES20.glEnableVertexAttribArray(attribPosition)
            GLES20.glEnableVertexAttribArray(attribTexCoord)

            posBuf.position(0)
            GLES20.glVertexAttribPointer(attribPosition, 2, GLES20.GL_FLOAT, false, 0, posBuf)
            GLES20.glVertexAttribPointer(attribTexCoord, 2, GLES20.GL_FLOAT, false, 0, texBuf)

            GLES20.glActiveTexture(GLES20.GL_TEXTURE0)
            GLES20.glBindTexture(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, textureId)
            GLES20.glUniform1i(uniformTex, 0)

            GLES20.glDrawArrays(GLES20.GL_TRIANGLE_STRIP, 0, 4)

            GLES20.glDisableVertexAttribArray(attribPosition)
            GLES20.glDisableVertexAttribArray(attribTexCoord)

        } catch (e: CameraNotAvailableException) {
            status("카메라 사용 불가: ${e.message ?: ""}")
            try { session.pause(); session.resume() } catch (_: Exception) {}
        } catch (e: Exception) {
            status("세션 업데이트 오류: ${e.javaClass.simpleName}${e.message?.let { " / $it" } ?: ""}")
        }
    }

    companion object {
        private const val VERT = """
            attribute vec2 aPosition;
            attribute vec2 aTexCoord;
            varying vec2 vTexCoord;
            void main() {
                vTexCoord = aTexCoord;
                gl_Position = vec4(aPosition, 0.0, 1.0);
            }
        """

        // OES 텍스처를 그리는 간단한 프래그먼트 셰이더
        private const val FRAG_OES = """
            #extension GL_OES_EGL_image_external : require
            precision mediump float;
            varying vec2 vTexCoord;
            uniform samplerExternalOES uTexture;
            void main() {
                gl_FragColor = texture2D(uTexture, vTexCoord);
            }
        """
    }
}

/** OpenGL 유틸들 (간단 버전) */
private object GLUtils {
    fun makeFloatBuffer(arr: FloatArray) =
        java.nio.ByteBuffer.allocateDirect(arr.size * 4)
            .order(java.nio.ByteOrder.nativeOrder())
            .asFloatBuffer()
            .put(arr)
            .apply { position(0) }

    fun buildProgram(vertSrc: String, fragSrc: String): Int {
        val vs = compileShader(GLES20.GL_VERTEX_SHADER, vertSrc)
        val fs = compileShader(GLES20.GL_FRAGMENT_SHADER, fragSrc)
        val prog = GLES20.glCreateProgram()
        GLES20.glAttachShader(prog, vs)
        GLES20.glAttachShader(prog, fs)
        GLES20.glLinkProgram(prog)
        val linkStatus = IntArray(1)
        GLES20.glGetProgramiv(prog, GLES20.GL_LINK_STATUS, linkStatus, 0)
        if (linkStatus[0] == 0) {
            val msg = GLES20.glGetProgramInfoLog(prog)
            GLES20.glDeleteProgram(prog)
            throw RuntimeException("Program link failed: $msg")
        }
        return prog
    }

    private fun compileShader(type: Int, src: String): Int {
        val shader = GLES20.glCreateShader(type)
        GLES20.glShaderSource(shader, src)
        GLES20.glCompileShader(shader)
        val compiled = IntArray(1)
        GLES20.glGetShaderiv(shader, GLES20.GL_COMPILE_STATUS, compiled, 0)
        if (compiled[0] == 0) {
            val msg = GLES20.glGetShaderInfoLog(shader)
            GLES20.glDeleteShader(shader)
            throw RuntimeException("Shader compile failed: $msg")
        }
        return shader
    }

    fun createOesTexture(): Int {
        val tex = IntArray(1)
        GLES20.glGenTextures(1, tex, 0)
        GLES20.glBindTexture(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, tex[0])
        GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_MIN_FILTER, GLES20.GL_LINEAR)
        GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_MAG_FILTER, GLES20.GL_LINEAR)
        GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_WRAP_S, GLES20.GL_CLAMP_TO_EDGE)
        GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_WRAP_T, GLES20.GL_CLAMP_TO_EDGE)
        return tex[0]
    }
}
