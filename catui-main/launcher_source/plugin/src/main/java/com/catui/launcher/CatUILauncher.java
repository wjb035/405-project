package com.catui.launcher;

import android.app.Activity;
import android.content.Intent;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageManager;
import android.content.pm.ResolveInfo;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.drawable.BitmapDrawable;
import android.graphics.drawable.Drawable;
import android.util.Base64;
import android.util.Log;

import org.godotengine.godot.Godot;
import org.godotengine.godot.plugin.GodotPlugin;
import org.godotengine.godot.plugin.UsedByGodot;

import java.io.ByteArrayOutputStream;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

public class CatUILauncher extends GodotPlugin {
    
    private static final String TAG = "CatUILauncher";
    
    public CatUILauncher(Godot godot) {
        super(godot);
        Log.d(TAG, "CatUILauncher: plugin initialized");
    }

    @Override
    public String getPluginName() {
        return "CatUILauncher";
    }

    @UsedByGodot
    public String[] getInstalledApps() {
        Log.d(TAG, "CatUILauncher: getInstalledApps called");
        Activity activity = getActivity();
        if (activity == null) {
            Log.e(TAG, "Activity is null");
            return new String[0];
        }

        PackageManager pm = activity.getPackageManager();
        Intent mainIntent = new Intent(Intent.ACTION_MAIN, null);
        mainIntent.addCategory(Intent.CATEGORY_LAUNCHER);
        
        List<ResolveInfo> apps = pm.queryIntentActivities(mainIntent, 0);
        List<String> appList = new ArrayList<>();
        Set<String> addedPackages = new HashSet<>();
        
        String myPackage = activity.getPackageName();
        
        for (ResolveInfo info : apps) {
            String packageName = info.activityInfo.packageName;
            
            if (packageName.equals(myPackage)) continue;
            if (addedPackages.contains(packageName)) continue;
            
            ApplicationInfo appInfo;
            try {
                appInfo = pm.getApplicationInfo(packageName, 0);
            } catch (PackageManager.NameNotFoundException e) {
                continue;
            }
            
            if ((appInfo.flags & ApplicationInfo.FLAG_SYSTEM) != 0) {
                if ((appInfo.flags & ApplicationInfo.FLAG_UPDATED_SYSTEM_APP) == 0) {
                    continue;
                }
            }
            
            String appName = pm.getApplicationLabel(appInfo).toString();
            appList.add(packageName + "|" + appName);
            addedPackages.add(packageName);
        }
        
        Collections.sort(appList, (a, b) -> {
            String nameA = a.split("\\|")[1].toLowerCase();
            String nameB = b.split("\\|")[1].toLowerCase();
            return nameA.compareTo(nameB);
        });
        
        return appList.toArray(new String[0]);
    }

    @UsedByGodot
    public String getAppIcon(String packageName, int size) {
        Activity activity = getActivity();
        if (activity == null) return "";
        
        try {
            PackageManager pm = activity.getPackageManager();
            Drawable icon = pm.getApplicationIcon(packageName);
            Bitmap bitmap = drawableToBitmap(icon, size);
            
            ByteArrayOutputStream stream = new ByteArrayOutputStream();
            bitmap.compress(Bitmap.CompressFormat.PNG, 100, stream);
            byte[] byteArray = stream.toByteArray();
            
            return Base64.encodeToString(byteArray, Base64.NO_WRAP);
        } catch (Exception e) {
            Log.e(TAG, "Failed to get icon for: " + packageName, e);
            return "";
        }
    }

    @UsedByGodot
    public boolean launchApp(String packageName) {
        Activity activity = getActivity();
        if (activity == null) return false;
        
        try {
            PackageManager pm = activity.getPackageManager();
            Intent intent = pm.getLaunchIntentForPackage(packageName);
            
            if (intent != null) {
                intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                activity.startActivity(intent);
                return true;
            }
        } catch (Exception e) {
            Log.e(TAG, "Failed to launch: " + packageName, e);
        }
        return false;
    }

    @UsedByGodot
    public boolean launchAppWithFile(String packageName, String filePath) {
        Activity activity = getActivity();
        if (activity == null) return false;
        
        try {
            Intent intent = new Intent(Intent.ACTION_VIEW);
            intent.setPackage(packageName);
            intent.setDataAndType(
                android.net.Uri.parse("file://" + filePath),
                getMimeType(filePath)
            );
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            // intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
            
            activity.startActivity(intent);
            return true;
        } catch (Exception e) {
            Log.w(TAG, "ACTION_VIEW failed, trying with extras: " + e.getMessage());
            
            // fallback: try sending the file path as an extra in the intent
            try {
                PackageManager pm = activity.getPackageManager();
                Intent intent = pm.getLaunchIntentForPackage(packageName);
                
                if (intent != null) {
                    intent.putExtra("ROM", filePath);
                    intent.putExtra("rom", filePath);
                    intent.putExtra("file", filePath);
                    intent.putExtra("path", filePath);
                    intent.putExtra(Intent.EXTRA_STREAM, android.net.Uri.parse("file://" + filePath));
                    intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                    activity.startActivity(intent);
                    return true;
                }
            } catch (Exception e2) {
                Log.e(TAG, "Failed to launch with file: " + packageName, e2);
            }
        }
        return false;
    }

    private String getMimeType(String filePath) {
        String extension = filePath.substring(filePath.lastIndexOf(".") + 1).toLowerCase();
        switch (extension) {
            case "sfc":
            case "smc":
            case "snes":
                return "application/x-snes-rom";
            case "nes":
                return "application/x-nes-rom";
            case "gba":
                return "application/x-gba-rom";
            case "gb":
            case "gbc":
                return "application/x-gameboy-rom";
            case "n64":
            case "z64":
            case "v64":
                return "application/x-n64-rom";
            case "iso":
            case "bin":
            case "cue":
                return "application/x-cd-image";
            default:
                return "application/octet-stream";
        }
    }

    @UsedByGodot
    public String getAppName(String packageName) {
        Activity activity = getActivity();
        if (activity == null) return packageName;
        
        try {
            PackageManager pm = activity.getPackageManager();
            ApplicationInfo info = pm.getApplicationInfo(packageName, 0);
            return pm.getApplicationLabel(info).toString();
        } catch (Exception e) {
            return packageName;
        }
    }

    private Bitmap drawableToBitmap(Drawable drawable, int size) {
        if (drawable instanceof BitmapDrawable) {
            Bitmap bmp = ((BitmapDrawable) drawable).getBitmap();
            return Bitmap.createScaledBitmap(bmp, size, size, true);
        }

        Bitmap bitmap = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888);
        Canvas canvas = new Canvas(bitmap);
        drawable.setBounds(0, 0, size, size);
        drawable.draw(canvas);
        return bitmap;
    }

    @UsedByGodot
    public String[] listFiles(String folderPath, String[] extensions) {
        Log.d(TAG, "listFiles called: " + folderPath);
        
        java.io.File folder = new java.io.File(folderPath);
        if (!folder.exists() || !folder.isDirectory()) {
            Log.e(TAG, "Folder does not exist or is not a directory: " + folderPath);
            return new String[0];
        }

        java.io.File[] files = folder.listFiles();
        if (files == null) {
            Log.e(TAG, "Could not list files in: " + folderPath);
            return new String[0];
        }

        List<String> result = new ArrayList<>();
        Set<String> extSet = new HashSet<>();
        
        if (extensions != null) {
            for (String ext : extensions) {
                extSet.add(ext.toLowerCase());
            }
        }

        for (java.io.File file : files) {
            if (file.isFile()) {
                String name = file.getName();
                String ext = "";
                int dotIndex = name.lastIndexOf('.');
                if (dotIndex > 0) {
                    ext = name.substring(dotIndex + 1).toLowerCase();
                }

                if (extSet.isEmpty() || extSet.contains(ext)) {
                    result.add(file.getAbsolutePath());
                }
            }
        }

        Log.d(TAG, "Found " + result.size() + " files");
        return result.toArray(new String[0]);
    }

    @UsedByGodot
    public boolean directoryExists(String path) {
        java.io.File folder = new java.io.File(path);
        return folder.exists() && folder.isDirectory();
    }

    @UsedByGodot
    public boolean fileExists(String path) {
        java.io.File file = new java.io.File(path);
        return file.exists() && file.isFile();
    }
}
