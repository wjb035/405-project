shader_type canvas_item;

uniform float progress: hint_range(0.0, 1.0) = 0.0;
uniform float ripple_count = 8.0;

void fragment() {
    vec2 uv = UV - 0.5;
    float dist = length(uv) / length(vec2(0.5, 0.5));
    
    // Hard solid circle
    float circle = step(dist, progress);
    
    // Sharp ripple rings ahead of circle
    float ahead = smoothstep(progress + 0.3, progress + 0.05, dist);
    float wave = step(0.5, sin((dist - progress) * ripple_count * 6.28318));
    float ripples = ahead * wave;
    
    float mask = clamp(circle + ripples, 0.0, 1.0);
    
    COLOR = vec4(0.0, 0.0, 0.0, mask);
}