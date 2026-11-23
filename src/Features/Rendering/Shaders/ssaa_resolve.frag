#version 330

in vec2 fragTexCoord;
out vec4 finalColor;

uniform sampler2D texture0;
uniform float invSampleCount;

void main()
{
    vec4 sum = texture(texture0, fragTexCoord);
    finalColor = vec4(clamp(sum.rgb * invSampleCount, 0.0, 1.0), 1.0);
}
