server {
    listen 127.0.0.1:50443 ssl;
    server_name localhost;
    ssl_certificate /etc/mydmsvn-local-sim/tls/nginx.crt;
    ssl_certificate_key /etc/mydmsvn-local-sim/tls/nginx.key;
    ssl_protocols TLSv1.2 TLSv1.3;

    if ($http_host != "localhost:50443") { return 444; }

    location / {
        proxy_pass http://127.0.0.1:5080;
        proxy_http_version 1.1;
        proxy_set_header Host localhost:50443;
        proxy_set_header X-Forwarded-For $remote_addr;
        proxy_set_header X-Forwarded-Proto https;
        proxy_set_header Forwarded "";
        proxy_set_header X-Forwarded-Host "";
        proxy_set_header X-Original-URL "";
        proxy_set_header X-Real-IP "";
        include /etc/nginx/mydmsvn-local-sim-proxy-token.conf;
        proxy_connect_timeout 5s;
        proxy_read_timeout 30s;
    }
}
