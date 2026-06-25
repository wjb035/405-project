shader_type canvas_item;

uniform float width : hint_range(0.0, 1.0) = 0.98;
uniform float radius : hint_range(0.0, 1.0) = 0.12;
uniform float stroke_width : hint_range(0.0, 0.25) = 0.025;
uniform float edge_softness : hint_range(0.0, 0.05) = 0.008;
uniform vec4 stroke_color : source_color = vec4(0.72, 0.64, 0.92, 1.0);

float square_rounded(vec2 uv, float width_value, float radius_value) {
	uv = uv * 2.0 - 1.0;

	radius_value *= width_value;
	vec2 abs_uv = abs(uv.xy) - radius_value;
	vec2 dist = vec2(max(abs_uv.xy, 0.0));
	float square = step(width_value - radius_value, length(dist));
	return square;
}

float square_stroke(vec2 uv, float width_value, float stroke_width_value) {
	uv = uv * 2.0 - 1.0;

	vec2 abs_uv = abs(uv.xy);
	float dist = max(abs_uv.x, abs_uv.y);
	return 1.0 - (step(width_value, dist) - step(width_value + stroke_width_value, dist));
}

float rounded_sdf(vec2 uv, float width_value, float radius_value) {
	uv = uv * 2.0 - 1.0;
	radius_value *= width_value;
	vec2 q = abs(uv) - vec2(width_value - radius_value);
	return length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0) - radius_value;
}

void fragment() {
	vec4 tex = texture(TEXTURE, UV);
	float outer_sdf = rounded_sdf(UV, width, radius);
	float inner_sdf = rounded_sdf(UV, max(width - stroke_width, 0.0), radius);
	float outer_mask = 1.0 - smoothstep(0.0, edge_softness, outer_sdf);
	float inner_mask = 1.0 - smoothstep(0.0, edge_softness, inner_sdf);
	float stroke_mask = clamp(outer_mask - inner_mask, 0.0, 1.0);

	vec4 clipped = vec4(tex.rgb, tex.a * outer_mask);
	vec4 stroke = vec4(stroke_color.rgb, stroke_color.a);
	COLOR = mix(clipped, stroke, stroke_mask * stroke_color.a);
	COLOR.a = max(clipped.a, stroke_mask * stroke_color.a);
}
