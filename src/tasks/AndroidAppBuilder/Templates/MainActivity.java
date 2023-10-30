// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

package net.dot;

import android.app.Activity;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.widget.RelativeLayout;
import android.widget.TextView;
import android.widget.Button;
import android.graphics.Color;
import android.view.View;

public class MainActivity extends Activity
{
    private static TextView textView;
    private static Button button;

    @Override
    protected void onCreate(Bundle savedInstanceState)
    {
        super.onCreate(savedInstanceState);

        textView = new TextView(this);
        textView.setTextSize(20);

        RelativeLayout rootLayout = new RelativeLayout(this);
        RelativeLayout.LayoutParams tvLayout =
                new RelativeLayout.LayoutParams(
                        RelativeLayout.LayoutParams.WRAP_CONTENT,
                        RelativeLayout.LayoutParams.WRAP_CONTENT);

        tvLayout.addRule(RelativeLayout.CENTER_HORIZONTAL);
        tvLayout.addRule(RelativeLayout.CENTER_VERTICAL);
        rootLayout.addView(textView, tvLayout);

        button = new Button(this);
        button.setText("Click Me!");

        RelativeLayout.LayoutParams buttonLayout =
                new RelativeLayout.LayoutParams(
                        RelativeLayout.LayoutParams.WRAP_CONTENT,
                        RelativeLayout.LayoutParams.WRAP_CONTENT);

        buttonLayout.addRule(RelativeLayout.BELOW, textView.getId());
        buttonLayout.addRule(RelativeLayout.CENTER_HORIZONTAL);
        rootLayout.addView(button, buttonLayout);

        setContentView(rootLayout);

        button.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View view) {
                MonoRunner.onClick();
            }
        });

        textView.setText("Initializing Native AOT runtime...");

        final Activity ctx = this;
        new Handler(Looper.getMainLooper()).postDelayed(new Runnable() {
            @Override
            public void run() {
                int retcode = MonoRunner.initialize(new String[0], ctx);
                textView.setText("Native AOT runtime initialized: " + retcode);
            }
        }, 1000);
    }

    public static void setText (String text) {
        button.setText (text);
    }
}
