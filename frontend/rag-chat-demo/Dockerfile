# Build context must be frontend/rag-chat-demo.
FROM node:24-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine AS final
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/rag-chat-demo/browser /usr/share/nginx/html
EXPOSE 80
