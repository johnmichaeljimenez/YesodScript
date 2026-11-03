#version 330
in vec2 fragTexCoord;
in vec3 fragNormal;
in vec4 fragColor;
in float fragLocalHeight;

uniform sampler2D texture0;
uniform vec4 colDiffuse;

out vec4 finalColor;

void main()
{
    vec4 texel = texture(texture0, fragTexCoord) * colDiffuse * fragColor;

    float ndot = clamp(dot(normalize(fragNormal), vec3(0.0, 1.0, 0.0)), 0.0, 1.0);
    float fakeLight = 0.65 + 0.35 * ndot; 
    
    vec3 base = texel.rgb * fakeLight;
    
    vec3 fogColor = vec3(0.55, 0.68, 0.78);
    float heightT = clamp((fragLocalHeight - 0.2) / (1.0 - 0.2), 0.0, 1.0);
    float smoothFog = 1.0 - smoothstep(0.2, 1.0, fragLocalHeight);
    float bottomFog = mix(smoothFog, 1.0 - heightT, 0.2);

    finalColor = vec4(mix(base, fogColor, bottomFog), texel.a);
}