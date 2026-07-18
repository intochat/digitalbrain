#version 460 core
#include <flutter/runtime_effect.glsl>

uniform vec2 uSize;
uniform vec2 uMouse;
uniform float uTime;
uniform vec4 uBrandColor;

out vec4 fragColor;

void main() {
    vec2 uv = FlutterFragCoord().xy / uSize;
    
    // Organic sub-surface backing glow
    float distToMouse = distance(FlutterFragCoord().xy, uMouse);
    float glow = smoothstep(220.0, 0.0, distToMouse) * 0.18;
    
    // Specular reflection (liquid glossy sheen)
    vec3 lightDir = normalize(vec3(uMouse - uSize / 2.0, 400.0));
    vec3 normal = vec3(0.0, 0.0, 1.0);
    vec3 viewDir = vec3(0.0, 0.0, 1.0);
    vec3 halfDir = normalize(lightDir + viewDir);
    float spec = pow(max(dot(normal, halfDir), 0.0), 48.0) * 0.30;
    
    // Frosted glass baseline acrylic
    vec4 baseColor = vec4(0.02, 0.02, 0.05, 0.50);
    
    // Mix body lighting
    vec4 col = baseColor + (uBrandColor * glow) + vec4(vec3(spec), 0.0);
    
    // Prismatic border edge highlight
    float borderThickness = 1.0;
    if (FlutterFragCoord().x < borderThickness || 
        FlutterFragCoord().y < borderThickness || 
        uSize.x - FlutterFragCoord().x < borderThickness || 
        uSize.y - FlutterFragCoord().y < borderThickness) {
        
        float blend = (uv.x + uv.y) * 0.5;
        vec4 prismColor = mix(vec4(1.0, 1.0, 1.0, 0.40), uBrandColor * vec4(1.0, 1.0, 1.0, 0.65), blend);
        col = mix(col, prismColor, 0.8);
    }
    
    fragColor = col;
}
