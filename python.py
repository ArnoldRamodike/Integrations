from fastapi import FastAPI, WebSocket
from fastapi.responses import JSONResponse
from sqlalchemy import create_engine, Column, Integer, String
from sqlalchemy.orm import sessionmaker
from sqlalchemy.ext.declarative import declarative_base
from pydantic import BaseModel
import sqlite3

app = FastAPI()

# SQLite database configuration
engine = create_engine("sqlite:///./database.db")
SessionLocal = sessionmaker(bind=engine)
Base = declarative_base()


# Models
class Post(Base):
    __tablename__ = "posts"
    id = Column(Integer, primary_key=True, index=True)
    title = Column(String)
    body = Column(String)


class PostCreate(BaseModel):
    title: str
    body: str


# RESTful API routes
@app.post("/posts/", response_model=Post)
async def create_post(post: PostCreate):
    async with JSONResponse.AsyncClient() as client:
        response = await client.get("https://jsonplaceholder.typicode.com/posts")
        posts = response.json()

    session = SessionLocal()
    try:
        for existing_post in session.query(Post).all():
            if existing_post.title == post.title and existing_post.body == post.body:
                return existing_post

        new_post = Post(title=post.title, body=post.body)
        session.add(new_post)
        session.commit()
        session.refresh(new_post)
        return new_post
    finally:
        session.close()


@app.get("/posts/{post_id}", response_model=Post)
def read_post(post_id: int):
    session = SessionLocal()
    try:
        post = session.query(Post).filter(Post.id == post_id).first()
        return post
    finally:
        session.close()


@app.put("/posts/{post_id}", response_model=Post)
def update_post(post_id: int, post: PostCreate):
    session = SessionLocal()
    try:
        existing_post = session.query(Post).filter(Post.id == post_id).first()
        existing_post.title = post.title
        existing_post.body = post.body
        session.commit()
        session.refresh(existing_post)
        return existing_post
    finally:
        session.close()


@app.delete("/posts/{post_id}")
def delete_post(post_id: int):
    session = SessionLocal()
    try:
        post = session.query(Post).filter(Post.id == post_id).first()
        session.delete(post)
        session.commit()
        return {"message": "Post deleted"}
    finally:
        session.close()


# WebSocket API routes
class WebSocketConnectionManager:
    def __init__(self):
        self.active_connections = []

    async def connect(self, websocket: WebSocket):
        await websocket.accept()
        self.active_connections.append(websocket)

    def disconnect(self, websocket: WebSocket):
        self.active_connections.remove(websocket)

    async def send_message(self, message: str, websocket: WebSocket):
        await websocket.send_text(message)

    async def broadcast(self, message: str):
        for connection in self.active_connections:
            await connection.send_text(message)


manager = WebSocketConnectionManager()


@app.websocket("/ws")
async def websocket_endpoint(websocket: WebSocket):
    await manager.connect(websocket)
    try:
        while True:
            data = await websocket.receive_text()
            await manager.send_message(f"You sent: {data}", websocket)
    except Exception as e:
        print(f"WebSocket error: {str(e)}")
    finally:
        manager.disconnect(websocket)


# Advanced WebSocket feature
@app.websocket("/websockets")
async def websocket_advanced_endpoint(websocket: WebSocket):
    await manager.connect(websocket)
    try:
        while True:
            data = await websocket.receive_json()
            session = SessionLocal()
            try:
                existing_post = session.query(Post).filter(Post.id == data["id"]).first()
                if existing_post:
                    existing_post.title = data["title"]
                    existing_post.body = data["body"]
                else:
                    new_post = Post(id=data["id"], title=data["title"], body=data["body"])
                    session.add(new_post)
                session.commit()
            finally:
                session.close()

            await manager.send_message("Post updated in the db.", websocket)
    except Exception as e:
        print(f"WebSocket error: {str(e)}")
    finally:
        manager.disconnect(websocket)
