shader_type canvas_item;

uniform float coils = 2.5;
uniform float wipe_blend = 0.0;

void fragment() {
    vec2 uv = UV - 0.5;
    float angle = atan(uv.y, uv.x) / (2.0 * 3.14159265); 
    float dist = length(uv) * 2.0;
    
	// spiral part
    float spiral = mod(angle - dist * coils, 1.0);
	
	float width = pow(dist, 1.5) * 0.4;
    spiral = smoothstep(0.5 - width, 0.5 + width, spiral);
	
	spiral = spiral * pow(dist, 2.0);
	
	// wipe part
	float wipe = (UV.x + UV.y) * 0.5; // 0 at top-left, 1 at bottom-right
   
	float final = mix(spiral, wipe, wipe_blend);

	float epsilon = 0.01;
	final = max(final, epsilon);
    COLOR = vec4(final, final, final, 1.0);
	}