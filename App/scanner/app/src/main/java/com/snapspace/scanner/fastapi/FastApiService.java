package com.snapspace.scanner.fastapi;

import com.google.gson.annotations.SerializedName;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.google.gson.annotations.SerializedName;

import java.util.List;
import java.util.concurrent.TimeUnit;

import okhttp3.RequestBody;
import okhttp3.MultipartBody;
import okhttp3.OkHttpClient;
import retrofit2.Retrofit;
import retrofit2.Call;
import retrofit2.converter.gson.GsonConverterFactory;
import retrofit2.converter.scalars.ScalarsConverterFactory;
import retrofit2.http.Header;
import retrofit2.http.Multipart;
import retrofit2.http.POST;
import retrofit2.http.Part;

public class FastApiService { 

    // 1. FastAPI로 보낼 서버 IP 주소 및 포트
    private static final String FAST_API_URL = "http://70.12.246.48:8000/";

    private static Retrofit retrofit;

    public static Retrofit getRetrofitInstance() {
        OkHttpClient client = new OkHttpClient.Builder()
                .connectTimeout(60, TimeUnit.SECONDS)
                .readTimeout(60,TimeUnit.SECONDS).build();
        if (retrofit == null) {
            Gson gson = new GsonBuilder().setLenient().create();
            retrofit = new retrofit2.Retrofit.Builder()
                    .baseUrl(FAST_API_URL).client(client)
                    .addConverterFactory(GsonConverterFactory.create(gson))
                    .build();
        }
        return retrofit;
    }


    public interface FastApiInterface { 

        @Multipart
        @POST("api/v1/upload/")
        Call<FastSuccessResponse> uploadFile( 
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

