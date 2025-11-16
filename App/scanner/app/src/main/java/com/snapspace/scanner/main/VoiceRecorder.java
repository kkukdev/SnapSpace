package com.snapspace.scanner.main;

import android.media.MediaRecorder;
import android.util.Log;

import java.io.File;
import java.io.IOException;

public class VoiceRecorder {
    private MediaRecorder mMediaRecorder;
    private String mFilePath;
    private boolean mIsRecording = false;
    private static final String TAG = "VoiceRecorder";

    public VoiceRecorder(String outputPath) {
        this.mFilePath = outputPath;
    }

    // 음성 녹음 시작
    public void startRecording() {
        try {
            File file = new File(mFilePath);
            file.getParentFile().mkdirs();

            mMediaRecorder = new MediaRecorder();
            mMediaRecorder.setAudioSource(MediaRecorder.AudioSource.MIC);
            mMediaRecorder.setOutputFormat(MediaRecorder.OutputFormat.THREE_GPP);
            mMediaRecorder.setAudioEncoder(MediaRecorder.AudioEncoder.AMR_NB);
            mMediaRecorder.setOutputFile(mFilePath);

            mMediaRecorder.prepare();
            mMediaRecorder.start();
            mIsRecording = true;

            Log.d(TAG, "음성 녹음 시작: " + mFilePath);
        } catch (IOException e) {
            Log.e(TAG, "음성 녹음 시작에 실패하였습니다: ", e);
        }
    }

    // 음성 녹음 중단 (성공적으로 녹음이 중단되었으면 true 반환)
    public boolean stopRecording() {
        if (!mIsRecording) {
            return false;
        }

        try {
            if (mMediaRecorder != null) {
                mMediaRecorder.stop();
                mMediaRecorder.release();
                mMediaRecorder = null;
            }
            mIsRecording = false;
            Log.d(TAG, "녹음이 중단되었습니다.");
            return true;
        } catch (RuntimeException e) {
            Log.e(TAG, "녹음 중단 과정에서 문제가 발생했습니다: ", e);
            return false;
        }
    }

    // 현재 녹음 상태 반환
    public boolean isRecording() {
        return mIsRecording;
    }

    // 녹음 중단 및 리소스 해제
    public void release() {
        if (mMediaRecorder != null) {
            try {
                if (mIsRecording) {
                    mMediaRecorder.stop();
                }
                mMediaRecorder.release();
            } catch (RuntimeException e) {
                Log.e(TAG, "리소스 해제 과정에서 오류가 발생했습니다: ", e);
            }
            mMediaRecorder = null;
            mIsRecording = false;
        }
    }
}
