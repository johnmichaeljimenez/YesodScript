#version 330
in vec2 fragTexCoord;
in vec3 fragNormal;
in vec4 fragColor;

uniform sampler2D texture0;
uniform vec4 colDiffuse;

out vec4 finalColor;

void main()
{
    vec4 texel = texture(texture0, fragTexCoord) * colDiffuse * fragColor;

    float ndot = clamp(dot(normalize(fragNormal), vec3(0.0, 1.0, 0.0)), 0.0, 1.0);
    float fakeLight = 0.65 + 0.35 * ndot; 
    finalColor = vec4(texel.rgb * fakeLight, texel.a);
}