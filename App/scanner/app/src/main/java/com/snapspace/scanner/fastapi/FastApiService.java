package com.snapspace.scanner.fastapi;

import com.google.gson.annotations.SerializedName;

import okhttp3.MultipartBody;
import okhttp3.RequestBody;
import retrofit2.Call;
import retrofit2.Retrofit;
import retrofit2.converter.gson.GsonConverterFactory;
import retrofit2.http.Multipart;
import retrofit2.http.POST;
import retrofit2.http.Part;

/**
 * FastAPI 서버와 통신하기 위한 Retrofit 서비스 클래스입니다.
 * (제공된 API 명세에 맞게 수정되었습니다.)
 */
public class FastApiService { // <-- 클래스 이름 변경

    // 1. FastAPI 서버의 기본 URL로 변경되었습니다. (새 Ngrok 주소)
    private static final String FAST_API_URL = "http://70.12.246.48:8000/";

    private static Retrofit retrofit;

    /**
     * Retrofit 인스턴스를 가져옵니다. (싱글톤 패턴)
     */
    public static Retrofit getRetrofitInstance() {
        OkHttpClient client = new OkHttpClient.Builder()
                .connectTimeout(60, TimeUnit.SECONDS)
                .readTimeout(60,TimeUnit.SECONDS).build();
        if (retrofit == null) {
            Gson gson = new GsonBuilder().setLenient().create();
            retrofit = new retrofit2.Retrofit.Builder()
                    .baseUrl(FAST_API_URL).client(client) // 1. 새 서버 주소로 설정
                    .addConverterFactory(GsonConverterFactory.create(gson)) // 2. JSON <-> Java 객체 변환기 설정
                    .build();
        }
        return retrofit;
    }

    /**
     * API 엔드포인트(URL 경로)를 정의하는 "메뉴판" 인터페이스입니다.
     */
    public interface FastApiInterface { // <-- 인터페이스 이름 변경

        /**
         * ZIP 파일을 Multipart POST로 업로드합니다.
         * (FastAPI 명세에 따라 수정됨)
         */
        @Multipart
        @POST("api/v1/upload/") // 2. POST 경로 수정 (기본 URL 뒤에 붙습니다)
        Call<FastSuccessResponse> uploadFile( // 3. 응답 모델을 FastSuccessResponse로 변경
                
                // 1. 파일(ZIP) 부분
                // 서버 명세서에서 파라미터 이름을 "file"로 요구하므로 @Part("file")로 지정합니다.
                @Part MultipartBody.Part file
                
                // 2. 서버 명세서에 다른 파라미터가 없으므로 제거합니다.
        );
    }

    /**
     * 업로드 성공 시(200) 서버로부터 받을 응답(JSON)을 매핑할 Java 객체(POJO)입니다.
     * (FastAPI 명세의 200 응답 스키마에 맞게 수정됨)
     */
    public static class FastSuccessResponse { // <-- 클래스 이름 변경
        
        @SerializedName("message")
        private String message;

        @SerializedName("success")
        private boolean success;

        @SerializedName("data")
        private ResponseData data;

        // --- Getter 메소드 ---
        public String getMessage() { return message; }
        public boolean isSuccess() { return success; }
        public ResponseData getData() { return data; }
    }

    /**
     * 성공 응답(JSON) 내의 "data" 객체를 매핑하기 위한 내부 클래스입니다.
     */
    public static class ResponseData {

        @SerializedName("original_filename")
        private String originalFilename;

        @SerializedName("saved_filename")
        private String savedFilename;

        @SerializedName("file_size")
        private long fileSize; // 숫자가 크므로 long 타입을 사용

        @SerializedName("file_path")
        private String filePath;

        // --- Getter 메소드 ---
        public String getOriginalFilename() { return originalFilename; }
        public String getSavedFilename() { return savedFilename; }
        public long getFileSize() { return fileSize; }
        public String getFilePath() { return filePath; }
    }

    /**
     * (참고) 업로드 실패 시(422) 받을 응답(JSON) 모델입니다.
     */
    public static class FastErrorResponse { // <-- 클래스 이름 변경
        @SerializedName("detail")
        private Object detail; // "detail" 필드가 복잡한 구조일 수 있으므로 Object로 받음

        public Object getDetail() { return detail; }
    }
}

