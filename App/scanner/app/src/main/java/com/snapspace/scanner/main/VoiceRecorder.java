package com.snapspace.scanner.main;

import android.media.AudioFormat;
import android.media.AudioRecord;
import android.media.MediaRecorder;
import android.util.Log;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.RandomAccessFile;

public class VoiceRecorder {
    private AudioRecord mAudioRecord;
    private String mFilePath;
    private boolean mIsRecording = false;
    private Thread mRecordingThread;
    private static final String TAG = "VoiceRecorder";
    
    // WAV 녹음 설정
    private static final int SAMPLE_RATE = 44100; // 44.1kHz
    private static final int CHANNEL_CONFIG = AudioFormat.CHANNEL_IN_MONO;
    private static final int AUDIO_FORMAT = AudioFormat.ENCODING_PCM_16BIT;

    public VoiceRecorder(String outputPath) {
        this.mFilePath = outputPath;
    }

    // 음성 녹음 시작
    public void startRecording() {
        try {
            File file = new File(mFilePath);
            file.getParentFile().mkdirs();

            // AudioRecord 버퍼 크기 계산
            int minBufferSize = AudioRecord.getMinBufferSize(SAMPLE_RATE, CHANNEL_CONFIG, AUDIO_FORMAT);
            if (minBufferSize == AudioRecord.ERROR || minBufferSize == AudioRecord.ERROR_BAD_VALUE) {
                Log.e(TAG, "버퍼 크기를 가져올 수 없습니다.");
                return;
            }
            
            // 버퍼를 2배로 설정하여 안정성 향상
            final int bufferSize = minBufferSize * 2;

            mAudioRecord = new AudioRecord(
                    MediaRecorder.AudioSource.MIC,
                    SAMPLE_RATE,
                    CHANNEL_CONFIG,
                    AUDIO_FORMAT,
                    bufferSize
            );

            if (mAudioRecord.getState() != AudioRecord.STATE_INITIALIZED) {
                Log.e(TAG, "AudioRecord 초기화 실패");
                return;
            }

            mAudioRecord.startRecording();
            mIsRecording = true;

            // 별도 스레드에서 녹음 데이터 처리
            mRecordingThread = new Thread(new Runnable() {
                @Override
                public void run() {
                    writeAudioDataToFile(bufferSize);
                }
            });
            mRecordingThread.start();

            Log.d(TAG, "음성 녹음 시작: " + mFilePath);
        } catch (Exception e) {
            Log.e(TAG, "음성 녹음 시작에 실패하였습니다: ", e);
            mIsRecording = false;
        }
    }

    // 오디오 데이터를 파일에 기록
    private void writeAudioDataToFile(int bufferSize) {
        byte[] data = new byte[bufferSize];
        FileOutputStream fos = null;
        
        try {
            fos = new FileOutputStream(mFilePath);
            
            // WAV 헤더 쓰기 (나중에 업데이트)
            writeWavHeader(fos, SAMPLE_RATE, CHANNEL_CONFIG, AUDIO_FORMAT);
            
            // 오디오 데이터 읽기 및 쓰기
            while (mIsRecording) {
                int bytesRead = mAudioRecord.read(data, 0, bufferSize);
                if (bytesRead > 0) {
                    fos.write(data, 0, bytesRead);
                }
            }
            
        } catch (IOException e) {
            Log.e(TAG, "파일 쓰기 오류: ", e);
        } finally {
            if (fos != null) {
                try {
                    fos.close();
                } catch (IOException e) {
                    Log.e(TAG, "파일 닫기 오류: ", e);
                }
            }
            
            // WAV 헤더 업데이트 (실제 데이터 크기 반영)
            updateWavHeader(mFilePath);
        }
    }

    // WAV 헤더 작성
    private void writeWavHeader(FileOutputStream fos, int sampleRate, int channelConfig, int audioFormat) throws IOException {
        int channels = (channelConfig == AudioFormat.CHANNEL_IN_MONO) ? 1 : 2;
        int bitsPerSample = (audioFormat == AudioFormat.ENCODING_PCM_16BIT) ? 16 : 8;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        int blockAlign = channels * bitsPerSample / 8;
        
        // RIFF 헤더
        fos.write(new byte[]{'R', 'I', 'F', 'F'});
        fos.write(intToByteArray(0)); // 파일 크기 (나중에 업데이트)
        fos.write(new byte[]{'W', 'A', 'V', 'E'});
        
        // fmt 청크
        fos.write(new byte[]{'f', 'm', 't', ' '});
        fos.write(intToByteArray(16)); // fmt 청크 크기
        fos.write(shortToByteArray((short) 1)); // PCM 포맷
        fos.write(shortToByteArray((short) channels)); // 채널 수
        fos.write(intToByteArray(sampleRate)); // 샘플 레이트
        fos.write(intToByteArray(byteRate)); // 바이트 레이트
        fos.write(shortToByteArray((short) blockAlign)); // 블록 정렬
        fos.write(shortToByteArray((short) bitsPerSample)); // 비트/샘플
        
        // data 청크
        fos.write(new byte[]{'d', 'a', 't', 'a'});
        fos.write(intToByteArray(0)); // 데이터 크기 (나중에 업데이트)
    }

    // WAV 헤더 업데이트 (실제 파일 크기 반영)
    private void updateWavHeader(String filePath) {
        try {
            RandomAccessFile wavFile = new RandomAccessFile(filePath, "rw");
            long fileSize = wavFile.length();
            long dataSize = fileSize - 44; // 헤더 크기 제외
            
            // 전체 파일 크기 업데이트 (RIFF 청크)
            wavFile.seek(4);
            wavFile.write(intToByteArray((int) (fileSize - 8)));
            
            // 데이터 크기 업데이트 (data 청크)
            wavFile.seek(40);
            wavFile.write(intToByteArray((int) dataSize));
            
            wavFile.close();
            Log.d(TAG, "WAV 헤더 업데이트 완료");
        } catch (IOException e) {
            Log.e(TAG, "WAV 헤더 업데이트 실패: ", e);
        }
    }

    // int를 리틀 엔디안 바이트 배열로 변환
    private byte[] intToByteArray(int value) {
        return new byte[]{
                (byte) (value & 0xFF),
                (byte) ((value >> 8) & 0xFF),
                (byte) ((value >> 16) & 0xFF),
                (byte) ((value >> 24) & 0xFF)
        };
    }

    // short를 리틀 엔디안 바이트 배열로 변환
    private byte[] shortToByteArray(short value) {
        return new byte[]{
                (byte) (value & 0xFF),
                (byte) ((value >> 8) & 0xFF)
        };
    }

    // 음성 녹음 중단 (성공적으로 녹음이 중단되었으면 true 반환)
    public boolean stopRecording() {
        if (!mIsRecording) {
            return false;
        }

        try {
            mIsRecording = false;
            
            // 녹음 스레드가 종료될 때까지 대기
            if (mRecordingThread != null) {
                try {
                    mRecordingThread.join(1000); // 최대 1초 대기
                } catch (InterruptedException e) {
                    Log.e(TAG, "스레드 대기 중 인터럽트 발생", e);
                }
                mRecordingThread = null;
            }
            
            if (mAudioRecord != null) {
                if (mAudioRecord.getState() == AudioRecord.STATE_INITIALIZED) {
                    mAudioRecord.stop();
                }
                mAudioRecord.release();
                mAudioRecord = null;
            }
            
            Log.d(TAG, "녹음이 중단되었습니다.");
            return true;
        } catch (Exception e) {
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
        if (mAudioRecord != null) {
            try {
                if (mIsRecording) {
                    stopRecording();
                }
            } catch (Exception e) {
                Log.e(TAG, "리소스 해제 과정에서 오류가 발생했습니다: ", e);
            }
            mAudioRecord = null;
            mIsRecording = false;
        }
    }
}
