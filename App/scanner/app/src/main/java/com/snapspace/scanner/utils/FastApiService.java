package com.snapspace.scanner.utils;

import android.content.Context;

import com.google.gson.annotations.SerializedName;
import com.google.gson.Gson;
import com.google.gson.GsonBuilder;

import java.util.concurrent.TimeUnit;

import okhttp3.MultipartBody;
import okhttp3.OkHttpClient;
import retrofit2.Retrofit;
import retrofit2.Call;
import retrofit2.converter.gson.GsonConverterFactory;
import retrofit2.http.Multipart;
import retrofit2.http.POST;
import retrofit2.http.Part;

public class FastApiService {

    private static Retrofit retrofit;
    private static String currentServerUrl = "";

    /**
     * Context를 받아서 동적으로 서버 URL을 설정
     */
    public static Retrofit getRetrofitInstance(Context context) {
        String serverUrl = ServerConfigManager.getServerUrl(context);
        
        android.util.Log.d("FastApiService", "사용할 서버 URL: " + serverUrl);

        // 서버 URL이 변경되었으면 Retrofit 인스턴스 재생성
        if (retrofit == null || !serverUrl.equals(currentServerUrl)) {
            currentServerUrl = serverUrl;

            OkHttpClient client = new OkHttpClient.Builder()
                    .connectTimeout(60, TimeUnit.SECONDS)
                    .readTimeout(60, TimeUnit.SECONDS)
                    .build();

            Gson gson = new GsonBuilder().setLenient().create();

            retrofit = new Retrofit.Builder()
                    .baseUrl(serverUrl)
                    .client(client)
                    .addConverterFactory(GsonConverterFactory.create(gson))
                    .build();
        }
        return retrofit;
    }

    public interface FastApiInterface {

        @Multipart
        @POST("api/v1/upload/")
        Call<FastSuccessResponse> uploadFile(
                @Part MultipartBody.Part model_type,
                @Part MultipartBody.Part group_name,
                @Part MultipartBody.Part file
        );
    }

    public static class FastSuccessResponse {

        @SerializedName("message")
        private String message;

        @SerializedName("success")
        private boolean success;

        @SerializedName("data")
        private ResponseData data;

        public String getMessage() { return message; }
        public boolean isSuccess() { return success; }
        public ResponseData getData() { return data; }
    }

    public static class ResponseData {

        @SerializedName("original_filename")
        private String originalFilename;

        @SerializedName("saved_filename")
        private String savedFilename;

        @SerializedName("file_size")
        private long fileSize;

        @SerializedName("file_path")
        private String filePath;

        public String getOriginalFilename() { return originalFilename; }
        public String getSavedFilename() { return savedFilename; }
        public long getFileSize() { return fileSize; }
        public String getFilePath() { return filePath; }
    }

    public static class FastErrorResponse {
        @SerializedName("detail")
        private Object detail;

        public Object getDetail() { return detail; }
    }
}