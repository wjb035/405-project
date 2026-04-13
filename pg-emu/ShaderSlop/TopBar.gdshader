shader_type canvas_item;

uniform float blur_amount : hint_range(0.0, 10.0) = 3.0;
uniform vec4 tint : source_color = vec4(0.08, 0.04, 0.12, 0.55);
uniform vec4 highlight_color : source_color = vec4(0.7, 0.5, 0.9, 0.4);
uniform float fade_strength : hint_range(0.0, 1.0) = 0.01;
uniform float highlight_thickness : hint_range(0.0, 1.0) = 0.02;
uniform sampler2D SCREEN_TEXTURE : hint_screen_texture, filter_linear_mipmap;

void fragment() {
 // --- Frosted glass blur ---
    vec4 blurred = vec4(0.0);
    vec2 offset = SCREEN_PIXEL_SIZE * blur_amount;
    for (float x = -1.0; x <= 1.0; x++) {
        for (float y = -1.0; y <= 1.0; y++) {
            blurred += texture(SCREEN_TEXTURE, SCREEN_UV + vec2(x, y) * offset);
        }
    }
    blurred /= 9.0;

    // --- Tint over blur ---
    vec3 col_rgb = mix(blurred.rgb, tint.rgb, tint.a);
	
    // --- Gradient fade on inner (bottom) edge ---
	float fade = mix(0.75, 1.0, smoothstep(0.0, fade_strength, UV.y));

    
    // --- Highlight ---
    if (UV.y > (1.0 - highlight_thickness)) {
        col_rgb = mix(col_rgb, highlight_color.rgb, highlight_color.a);
    }

    COLOR = vec4(col_rgb, fade);
}