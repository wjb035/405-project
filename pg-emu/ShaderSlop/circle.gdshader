shader_type canvas_item;

uniform float width : hint_range(0.0, 1.0) = 0.96;
uniform float stroke_width : hint_range(0.0, 0.25) = 0.026;
uniform float edge_softness : hint_range(0.0, 0.05) = 0.006;
uniform vec4 stroke_color : source_color = vec4(0.72, 0.64, 0.92, 1.0);

void fragment() {
	vec2 uv = UV - vec2(0.5);
	float dist = length(uv) * 2.0;
	float outer_mask = 1.0 - smoothstep(width, width + edge_softness, dist);
	float inner_width = max(width - stroke_width, 0.0);
	float inner_mask = 1.0 - smoothstep(inner_width, inner_width + edge_softness, dist);
	float stroke_mask = clamp(outer_mask - inner_mask, 0.0, 1.0);
	vec4 tex = texture(TEXTURE, UV);
	vec4 clipped = vec4(tex.rgb, tex.a * outer_mask);
	vec4 stroke = vec4(stroke_color.rgb, stroke_color.a);
	
	vec4 base = COLOR;
		
	vec4 final_col = mix(clipped, stroke, stroke_mask * stroke_color.a);
	COLOR.a = max(clipped.a, stroke_mask * stroke_color.a);
}
