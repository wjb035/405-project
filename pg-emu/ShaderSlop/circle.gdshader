shader_type canvas_item;

void fragment() {
    vec2 uv = UV - vec2(0.5);
    float dist = length(uv);
    float alpha = 1.0 - smoothstep(0.48, 0.5, dist);
    vec4 tex = texture(TEXTURE, UV);
    COLOR = tex * COLOR; 
    COLOR.a *= alpha;
}