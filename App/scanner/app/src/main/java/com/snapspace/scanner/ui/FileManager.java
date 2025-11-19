package com.snapspace.scanner.ui;

import android.Manifest;
import android.app.AlertDialog;
import android.app.Dialog;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.drawable.Drawable;
import android.net.Uri;
import android.os.Bundle;
import android.preference.PreferenceManager;
import android.text.Html;
import android.util.Log;
import android.view.LayoutInflater;
import android.view.MotionEvent;
import android.view.View;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.GridView;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.RelativeLayout;
import android.widget.TextView;
import android.widget.Toast;

import com.google.ar.core.ArCoreApk;
import com.snapspace.scanner.R;
import com.snapspace.scanner.main.Exporter;
import com.snapspace.scanner.main.Main;
import com.lvonasek.utils.Compatibility;
import com.lvonasek.utils.IO;

import java.io.File;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.Locale;

public class FileManager extends AbstractActivity implements View.OnClickListener {
  private FileAdapter mAdapter;
  private GridView mList;
  private ProgressBar mProgress;
  private TextView mText;
  private LinearLayout mOptions;
  private View mRename;
  private View mShare;
  private LinearLayout mShareLayout;
  private LinearLayout mRenameLayout;

  @Override
  protected void onCreate(Bundle savedInstanceState) {
    super.onCreate(savedInstanceState);
    setContentView(R.layout.activity_files);

    boolean showPro = Compatibility.isPlayStoreSupported(this) && !isProVersion(this);

    mRename = findViewById(R.id.rename);
    mRenameLayout = findViewById(R.id.rename_layout);

    mShare = findViewById(R.id.share);
    mShareLayout = findViewById(R.id.share_layout);

    mOptions = findViewById(R.id.options);

    mRename.setOnClickListener(this);
    mShare.setOnClickListener(this);
    findViewById(R.id.delete).setOnClickListener(this);
    findViewById(R.id.delete_layout).setOnClickListener(this);

    mList = findViewById(R.id.list);
    mText = findViewById(R.id.info_text);
    mProgress = findViewById(R.id.progressBar);

    int columns = 3;
    SharedPreferences pref = PreferenceManager.getDefaultSharedPreferences(this);
    columns = pref.getInt(getString(R.string.pref_layout), columns);

    mAdapter = new FileAdapter(this, columns);
    mList.setOnTouchListener((view, event) -> {
      mAdapter.forwardTouch(event);
      return false;
    });
  }

  @Override
  public void onBackPressed() {
    if (mProgress.getVisibility() == View.VISIBLE) {
      System.exit(0);
    } else if (mAdapter.hasParent()) {
      mAdapter.toParent();
    } else if (mAdapter.getSelected() != null) {
      mAdapter.update();
    } else {
      finish();
    }
  }

  @Override
  public int getNavigationBarColor() {
        return Color.BLACK;
    }

  @Override
  public int getStatusBarColor() {
    return Color.argb(255, 48, 48, 48);
  }

  @Override
  protected void onResume() {
    super.onResume();

    refreshUI();
  }

  public void refreshUI() {
    long time = System.currentTimeMillis();
    boolean migrate = hasFilesToMigrate(this);
    if (migrate) {
      Log.d(TAG, "Some files has to be migrated");
    }

    mList.setVisibility(View.VISIBLE);
    mText.setOnClickListener(null);
    mText.setText(migrate ? R.string.migrating_data : R.string.wait);
    mText.setVisibility(mAdapter.isEmpty() ? View.VISIBLE : View.GONE);

    new Thread(() -> {
      //update file structure
      Exporter.makeStructure(getPath(migrate));

      //get list of files
      runOnUiThread(() -> {
        mAdapter.update();
        Log.d(TAG, "Listing files took " + (System.currentTimeMillis() - time) + "ms");

        mText.setText(R.string.no_data);
        mText.setVisibility(mAdapter.getCount() == 0 ? View.VISIBLE : View.GONE);
        mList.setAdapter(mAdapter);
        mProgress.setVisibility(View.GONE);

        mAdapter.notifyDataSetChanged();
        if (mAdapter.getCount() > 0) {
          mList.setSelection(0);
        }
      });
    }).start();
  }

  public void showProgress()
  {
    try {
      mProgress.setVisibility(View.VISIBLE);
    } catch (Exception e) {
      e.printStackTrace();
    }
  }

  @Override
  public void onClick(View v) {
    int id = v.getId();

    if (id == R.id.delete || id == R.id.delete_layout) {
      mAdapter.deleteModel();
    } else if (id == R.id.rename || id == R.id.rename_layout) {
      mAdapter.rename();
    } else if (id == R.id.share || id == R.id.share_layout) {
      mAdapter.shareModel();
    }
  }

  public void setColumns(int count) {
    mList.setNumColumns(count);

    SharedPreferences.Editor e = PreferenceManager.getDefaultSharedPreferences(this).edit();
    e.putInt(getString(R.string.pref_layout), count);
    e.commit();
  }

  public void setOptions(int size) {
    boolean on = size > 0;

    mOptions.setVisibility(on ? View.VISIBLE : View.GONE);

    boolean more = size > 1;
    boolean ext = mAdapter.hasExtension();

    mRename.setVisibility(!more ? View.VISIBLE : View.GONE);
    mRenameLayout.setVisibility(!more ? View.VISIBLE : View.GONE);
    mShare.setVisibility(ext && !more ? View.VISIBLE : View.GONE);
    mShareLayout.setVisibility(!more ? View.VISIBLE : View.GONE);
  }
}
