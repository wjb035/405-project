shader_type canvas_item;

uniform float speed : hint_range(0.1, 5.0) = 1.0;

void fragment() {
	vec2 uv = UV - 0.5;
	float angle = atan(uv.y, uv.x);
	float time_offset = TIME * speed;
	
	float gradient_pos = fract((angle / (2.0 * PI)) + 0.5 + time_offset * 0.15);
	
	vec3 green = vec3(0.0, 1.0, 0.0);
	vec3 yellow = vec3(1.0, 1.0, 0.0);
	vec3 white = vec3(1.0, 1.0, 0.7);
	
	vec3 color;
	if (gradient_pos < 0.33) {
		float t = smoothstep(0.0, 1.0, gradient_pos / 0.33);
		color = mix(green, white, t);
	} else if (gradient_pos < 0.66) {
		float t = smoothstep(0.0, 1.0, (gradient_pos - 0.33) / 0.33);
		color = mix(white, yellow, t);
	} else {
		float t = smoothstep(0.0, 1.0, (gradient_pos - 0.66) / 0.34);
		color = mix(yellow, green, t);
	}
	
	COLOR = vec4(color, COLOR.a);
}
