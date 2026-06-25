shader_type canvas_item;

uniform float ripples = 8.0;
uniform float ripple_strength = 0.05;

void fragment() {
    vec2 uv = UV - 0.5;
    float dist = length(uv) / length(vec2(0.5, 0.5));
    
    // Add ripple by offsetting dist with a sine wave
    float ripple = dist + sin(dist * ripples * 3.14159) * ripple_strength;
    
    // Invert so center is white, edges dark
    float mask = 1.0 - ripple;
    mask = clamp(mask, 0.0, 1.0);
    
    COLOR = vec4(mask, mask, mask, 1.0);
}