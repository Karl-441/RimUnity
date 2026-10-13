#version 450
layout(location = 0) in vec3 inPosition; // @vertex Position required
layout(location = 1) in vec2 inUv; // @vertex TexCoord0 optional @default = vec2(0.0, 0.0)
layout(push_constant) uniform Push { mat4 modelMatrix; } push;
layout(set = 0, binding = 0) uniform VertexUniforms { mat4 ViewProj; } view;
layout(location = 0) out vec2 uv;

//使用 Waylith 提交的模型和相机矩阵。
void main()
{
    gl_Position = view.ViewProj * push.modelMatrix * vec4(inPosition, 1.0);
    uv = inUv;
}
