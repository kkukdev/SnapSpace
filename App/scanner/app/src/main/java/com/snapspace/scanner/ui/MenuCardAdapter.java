package com.snapspace.scanner.ui;

import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.ImageView;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.cardview.widget.CardView;
import androidx.recyclerview.widget.RecyclerView;

import com.snapspace.scanner.R;

import java.util.List;

public class MenuCardAdapter extends RecyclerView.Adapter<MenuCardAdapter.CardViewHolder> {

    private List<MenuCard> menuCards;
    private OnCardClickListener listener;

    public interface OnCardClickListener {
        void onCardClick(MenuCard card);
    }

    public MenuCardAdapter(List<MenuCard> menuCards, OnCardClickListener listener) {
        this.menuCards = menuCards;
        this.listener = listener;
    }

    @NonNull
    @Override
    public CardViewHolder onCreateViewHolder(@NonNull ViewGroup parent, int viewType) {
        View view = LayoutInflater.from(parent.getContext())
                .inflate(R.layout.item_menu_card, parent, false);
        return new CardViewHolder(view);
    }

    @Override
    public void onBindViewHolder(@NonNull CardViewHolder holder, int position) {
        MenuCard card = menuCards.get(position);
        holder.bind(card, listener);
    }

    @Override
    public int getItemCount() {
        return menuCards.size();
    }

    static class CardViewHolder extends RecyclerView.ViewHolder {
        ImageView cardIcon;
        TextView cardTitle;
        TextView cardDescription;
        CardView cardView;

        public CardViewHolder(@NonNull View itemView) {
            super(itemView);
            cardIcon = itemView.findViewById(R.id.card_icon);
            cardTitle = itemView.findViewById(R.id.card_title);
            cardDescription = itemView.findViewById(R.id.card_description);
            cardView = (CardView) itemView;
        }

        public void bind(MenuCard card, OnCardClickListener listener) {
            cardIcon.setImageResource(card.getIconResId());
            cardTitle.setText(card.getTitle());
            cardDescription.setText(card.getDescription());

            cardView.setOnClickListener(v -> {
                if (listener != null) {
                    listener.onCardClick(card);
                }
            });
        }
    }
}