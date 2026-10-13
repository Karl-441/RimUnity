#version 450
layout(location = 0) in vec3 inPosition; // @vertex Position required
layout(location = 1) in vec4 inUv; // @vertex TexCoord0 required
layout(location = 2) in vec4 inColor; // @vertex Color required
layout(location = 3) in vec4 inAnimation; // @vertex TexCoord1 optional @default = vec4(0.0, 0.0, 0.0, 0.0)
layout(push_constant) uniform Push { mat4 modelMatrix; } push;
layout(set = 0, binding = 0) uniform VertexUniforms { mat4 ViewProj; } view;
layout(location = 0) out vec4 uv;
layout(location = 1) out vec4 color;
layout(location = 2) out float frameBlend;
layout(location = 3) out vec3 worldPosition;
//TEXCOORD0 的后两维保存下一帧 UV，TEXCOORD1.x 保存插值因子。
void main()
{
    vec4 world = push.modelMatrix * vec4(inPosition, 1.0);
    gl_Position = view.ViewProj * world;
    worldPosition = world.xyz;
    uv = inUv; color = inColor; frameBlend = inAnimation.x;
}
