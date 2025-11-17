package com.snapspace.scanner.main;

import android.app.Activity;
import android.app.ActivityManager;
import android.view.View;
import android.widget.TextView;

import com.snapspace.scanner.ui.AbstractActivity;

public class Indicators implements Runnable {

    private ActivityManager mActivityManager;
    private ActivityManager.MemoryInfo mMemoryInfo;
    private AbstractActivity mMain;
    private String mOverrideMessage;
    private boolean mRunning;

    private TextView mInfoLog;

    public Indicators(AbstractActivity main) {
        mMain = main;
        mOverrideMessage = null;
        mRunning = true;
        mInfoLog = main.findViewById(com.snapspace.scanner.R.id.infolog);
        mInfoLog = main.findViewById(com.snapspace.scanner.R.id.infolog);

        mActivityManager = (ActivityManager) main.getSystemService(Activity.ACTIVITY_SERVICE);
        mMemoryInfo = new ActivityManager.MemoryInfo();

        new Thread(this).start();
    }

    public void disable() {
        mRunning = false;
    }

    @Override
    public void run()
    {
        while (mRunning) {
            try
            {
                Thread.sleep(1000);
            } catch (InterruptedException e)
            {
                e.printStackTrace();
            }
            mMain.runOnUiThread(() -> {
                //memory info
                mActivityManager.getMemoryInfo(mMemoryInfo);
                long freeMBs = mMemoryInfo.availMem / 1048576L;

                //update info about AR
                updateText(JNI.getEvent(mMain.getResources()));
            });
        }
    }

    private void updateText(String text) {
        if (mOverrideMessage != null) {
            text = mOverrideMessage;
        }
        mInfoLog.setVisibility(text.length() > 0 ? View.VISIBLE : View.GONE);
        mInfoLog.setText(text);
    }
}
