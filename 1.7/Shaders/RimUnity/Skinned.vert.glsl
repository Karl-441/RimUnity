#version 450
layout(location = 0) in vec3 inPosition; // @vertex Position required
layout(location = 1) in vec2 inUv; // @vertex TexCoord0 optional @default = vec2(0.0, 0.0)
layout(location = 2) in vec4 boneRange; // @vertex TexCoord6 required
layout(push_constant) uniform Push { mat4 modelMatrix; } push;
layout(set = 0, binding = 0) uniform VertexUniforms { mat4 ViewProj; } view;
layout(std430, set = 2, binding = 2) readonly buffer _Bones { mat4 matrices[]; } bones;
layout(std430, set = 2, binding = 3) readonly buffer _SkinWeights { vec4 influences[]; } weights;
layout(location = 0) out vec2 uv;
//Unity Matrix4x4 的列优先存储直接对应 GLSL mat4。
void main()
{
    mat4 skin = mat4(0.0);
    for (int i = 0; i < int(boneRange.y); ++i)
    {
        vec4 influence = weights.influences[int(boneRange.x) + i];
        skin += bones.matrices[int(influence.x)] * influence.y;
    }
    if (boneRange.y == 0.0) skin = mat4(1.0);
    gl_Position = view.ViewProj * push.modelMatrix * skin * vec4(inPosition, 1.0);
    uv = inUv;
}
