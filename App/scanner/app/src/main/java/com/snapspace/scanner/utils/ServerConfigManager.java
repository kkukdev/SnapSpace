package com.snapspace.scanner.utils;

import android.content.Context;
import android.content.SharedPreferences;
import android.preference.PreferenceManager;

public class ServerConfigManager {

    private static final String PREF_SERVER_IP = "server_ip";
    private static final String PREF_SERVER_PORT = "server_port";
    private static final String DEFAULT_IP = "0.0.0.0";
    private static final String DEFAULT_PORT = "8000";

    /**
     * 서버 설정 저장
     */
    public static void saveConfig(Context context, String ip, String port) {
        SharedPreferences prefs = PreferenceManager.getDefaultSharedPreferences(context);
        prefs.edit()
                .putString(PREF_SERVER_IP, ip)
                .putString(PREF_SERVER_PORT, port)
                .apply();
    }

    /**
     * 서버 IP 가져오기
     */
    public static String getServerIp(Context context) {
        SharedPreferences prefs = PreferenceManager.getDefaultSharedPreferences(context);
        return prefs.getString(PREF_SERVER_IP, DEFAULT_IP);
    }

    /**
     * 서버 포트 가져오기
     */
    public static String getServerPort(Context context) {
        SharedPreferences prefs = PreferenceManager.getDefaultSharedPreferences(context);
        return prefs.getString(PREF_SERVER_PORT, DEFAULT_PORT);
    }

    /**
     * 완전한 서버 URL 반환 (http://ip:port/)
     */
    public static String getServerUrl(Context context) {
        String ip = getServerIp(context);
        String port = getServerPort(context);
        return "http://" + ip + ":" + port + "/";
    }

    /**
     * IP 주소 유효성 검사
     */
    public static boolean isValidIp(String ip) {
        if (ip == null || ip.isEmpty()) {
            return false;
        }
        String[] parts = ip.split("\\.");
        if (parts.length != 4) {
            return false;
        }
        try {
            for (String part : parts) {
                int num = Integer.parseInt(part);
                if (num < 0 || num > 255) {
                    return false;
                }
            }
            return true;
        } catch (NumberFormatException e) {
            return false;
        }
    }

    /**
     * 포트 번호 유효성 검사
     */
    public static boolean isValidPort(String port) {
        if (port == null || port.isEmpty()) {
            return false;
        }
        try {
            int portNum = Integer.parseInt(port);
            return portNum >= 1 && portNum <= 65535;
        } catch (NumberFormatException e) {
            return false;
        }
    }
}