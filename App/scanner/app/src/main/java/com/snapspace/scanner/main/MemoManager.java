package com.snapspace.scanner.main;

import android.util.Log;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.io.FileWriter;
import java.util.ArrayList;
import java.util.List;

public class MemoManager {
    private static final String TAG = "MemoManager";
    private List<MemoItem> mMemos;
    private String mJsonFilePath;

    public MemoManager() {
        mMemos = new ArrayList<>();
    }

    /**
     * JSON 파일 경로 설정
     */
    public void setJsonFilePath(String path) {
        this.mJsonFilePath = path;
    }

    /**
     * 음성 메모 추가
     */
    public void addAudioMemo(String anchor, String audioFileName) {
        MemoItem item = new MemoItem();
        item.type = "audio";
        item.anchor = anchor;
        item.content = audioFileName;

        mMemos.add(item);
        Log.d(TAG, "Audio memo added: " + audioFileName + " at " + anchor);
    }

    /**
     * 텍스트 메모 추가 (나중에 사용)
     */
    public void addTextMemo(String anchor, String text) {
        MemoItem item = new MemoItem();
        item.type = "text";
        item.anchor = anchor;
        item.content = text;

        mMemos.add(item);
        Log.d(TAG, "Text memo added at " + anchor);
    }

    /**
     * 모든 메모를 JSON 파일로 저장
     */
    public boolean saveToJson() {
        if (mJsonFilePath == null || mJsonFilePath.isEmpty()) {
            Log.e(TAG, "JSON file path not set");
            return false;
        }

        if (mMemos.isEmpty()) {
            Log.d(TAG, "No memos to save");
            return true; // 메모가 없으면 파일을 만들지 않음
        }

        try {
            JSONObject root = new JSONObject();
            JSONArray memosArray = new JSONArray();

            for (MemoItem memo : mMemos) {
                JSONObject item = new JSONObject();
                item.put("type", memo.type);
                item.put("anchor", memo.anchor);
                item.put("content", memo.content);
                memosArray.put(item);
            }

            root.put("memos", memosArray);

            // 파일 쓰기
            File jsonFile = new File(mJsonFilePath);
            FileWriter writer = new FileWriter(jsonFile);
            writer.write(root.toString(4)); // 4칸 들여쓰기로 pretty print
            writer.close();

            Log.d(TAG, "Memos saved to: " + mJsonFilePath);
            return true;

        } catch (Exception e) {
            Log.e(TAG, "Failed to save memos to JSON", e);
            return false;
        }
    }

    /**
     * 메모 개수 반환
     */
    public int getMemoCount() {
        return mMemos.size();
    }

    /**
     * 모든 메모 초기화
     */
    public void clear() {
        mMemos.clear();
        Log.d(TAG, "All memos cleared");
    }

    /**
     * 메모 아이템 내부 클래스
     */
    public static class MemoItem {
        public String type;      // "audio" or "text"
        public String anchor;    // "x:0.44,y:0.51,z:22.00"
        public String content;   // 파일명 또는 텍스트 내용
    }
}