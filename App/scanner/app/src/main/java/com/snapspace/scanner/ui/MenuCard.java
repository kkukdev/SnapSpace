package com.snapspace.scanner.ui;

public class MenuCard {
    private int iconResId;
    private String title;
    private String description;
    private MenuType type;

    public enum MenuType {
        SPACE_SCAN,
        OBJECT_SCAN,
        PREVIEW,
    }

    public MenuCard(int iconResId, String title, String description, MenuType type) {
        this.iconResId = iconResId;
        this.title = title;
        this.description = description;
        this.type = type;
    }

    public int getIconResId() {
        return iconResId;
    }

    public String getTitle() {
        return title;
    }

    public String getDescription() {
        return description;
    }

    public MenuType getType() {
        return type;
    }
}