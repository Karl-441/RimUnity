#version 450
layout(location = 0) in vec3 inPosition; // @vertex Position required
layout(location = 1) in vec2 inUv; // @vertex TexCoord0 required
layout(location = 2) in vec4 inColor; // @vertex Color required
layout(push_constant) uniform Push { mat4 modelMatrix; } push;
layout(set = 0, binding = 0) uniform VertexUniforms { mat4 ViewProj; } view;
layout(location = 0) out vec2 uv;
layout(location = 1) out vec4 color;
//粒子顶点已在世界空间生成，仍保留模型矩阵入口供复用。
void main() { gl_Position = view.ViewProj * push.modelMatrix * vec4(inPosition, 1.0); uv = inUv; color = inColor; }
